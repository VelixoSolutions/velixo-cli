// Inventory checker: a full-screen kiosk that looks up a scanned item in Acumatica through the Velixo CLI,
// and can move units of it to a reserved location.
// No dependencies: Node.js 20 or later, and a signed-in `velixo` on the PATH.

import { execFile } from 'node:child_process';
import { createReadStream } from 'node:fs';
import { mkdir, readFile, stat } from 'node:fs/promises';
import { timingSafeEqual } from 'node:crypto';
import { createServer } from 'node:http';
import { createServer as createSecureServer } from 'node:https';
import { extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));

const settings = {
    port: Number(process.env.PORT ?? 8080),
    host: process.env.HOST ?? '127.0.0.1',
    velixo: process.env.VELIXO_BIN ?? 'velixo',
    connection: process.env.VELIXO_CONNECTION,
    intent: process.env.KIOSK_INTENT ?? 'Look up a scanned item at the in-store inventory kiosk',
    imageCache: resolve(process.env.KIOSK_IMAGE_CACHE ?? join(here, '.cache', 'images')),
    warehouse: process.env.KIOSK_WAREHOUSE,
    reserveLocation: process.env.KIOSK_RESERVE_LOCATION ?? 'RESERVED',
    reserveIntent: process.env.KIOSK_RESERVE_INTENT ?? 'Move units of a scanned item to the reserved location from the in-store kiosk',
    password: process.env.KIOSK_PASSWORD,
    tlsCert: process.env.KIOSK_TLS_CERT,
    tlsKey: process.env.KIOSK_TLS_KEY,
};

const files = {
    itemQuery: join(here, 'velixo', 'item.vql'),
    onOrderQuery: join(here, 'velixo', 'on-order.vql'),
    warehousesQuery: join(here, 'velixo', 'warehouses.vql'),
    imageBatch: join(here, 'velixo', 'item-image.batch.json'),
    locationsQuery: join(here, 'velixo', 'locations.vql'),
    reserveBatch: join(here, 'velixo', 'reserve-transfer.batch.json'),
    publicDir: join(here, 'public'),
};

const contentTypes = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.webmanifest': 'application/manifest+json; charset=utf-8',
    '.svg': 'image/svg+xml',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.webp': 'image/webp',
    '.bmp': 'image/bmp',
    '.avif': 'image/avif',
};

// An Acumatica inventory ID: letters, digits and the separators segmented keys use. Scanners send nothing else.
const inventoryIdPattern = /^[A-Za-z0-9][A-Za-z0-9 _\-.\/]{0,29}$/;

class VelixoError extends Error {
    constructor(code, message, exitCode) {
        super(message);
        this.code = code;
        this.exitCode = exitCode;
    }
}

function velixo(args, intent = settings.intent) {
    const fullArgs = [
        ...args,
        '--output', 'json',
        '--quiet',
        '--non-interactive',
        '--intent', intent,
        ...(settings.connection ? ['--connection', settings.connection] : []),
    ];

    return new Promise((resolvePromise, reject) => {
        execFile(settings.velixo, fullArgs, { timeout: 90_000, maxBuffer: 16 * 1024 * 1024 }, (error, stdout) => {
            let document;
            try {
                document = JSON.parse(stdout);
            } catch {
                document = undefined;
            }

            if (document?.error) {
                reject(new VelixoError(document.error.code, document.error.message, document.error.exitCode));
            } else if (error) {
                reject(new VelixoError(error.code === 'ENOENT' ? 'CLI_NOT_FOUND' : 'CLI_FAILED', error.message, error.code));
            } else {
                resolvePromise(document);
            }
        });
    });
}

/** Runs a .vql file and returns its rows as objects keyed by column caption. */
async function runQuery(file, parameters) {
    const args = ['query', 'run', '--query', file];
    for (const [label, value] of Object.entries(parameters)) {
        args.push('--param', `${label}=${value}`);
    }

    const document = await velixo(args);
    const { columns, rows } = document.result.data;
    return rows.map(row => Object.fromEntries(columns.map((column, index) => [column.caption, row[index]])));
}

async function lookUpItem(inventoryId) {
    const [item] = await runQuery(files.itemQuery, { 'Inventory ID': inventoryId });
    if (!item) {
        return undefined;
    }

    const [receipts, warehouses] = await Promise.all([
        runQuery(files.onOrderQuery, { 'Inventory ID': inventoryId }),
        runQuery(files.warehousesQuery, { 'Inventory ID': inventoryId }),
    ]);
    const imageFile = imageFileOf(item['Image URL']);

    return {
        inventoryId: item['Inventory ID'].trim(),
        description: item['Description'],
        baseUnit: item['Base Unit'],
        qtyOnHand: item['Qty. On Hand'],
        qtyAvailable: item['Qty. Available'],
        image: imageFile ? `/api/items/${encodeURIComponent(inventoryId)}/image?file=${encodeURIComponent(imageFile)}` : null,
        warehouses: warehouses.map(warehouse => ({
            warehouse: warehouse['Warehouse'].trim(),
            name: warehouse['Warehouse Name'],
            qtyOnHand: warehouse['Qty. On Hand'],
            qtyAvailable: warehouse['Qty. Available'],
            isThisStore: warehouse['Warehouse'].trim() === settings.warehouse,
        })),
        onOrder: receipts.map(receipt => ({
            orderNbr: receipt['Order Nbr.'],
            promisedDate: receipt['Promised Date'],
            quantity: receipt['Quantity Still to Receive (Base)'],
            vendor: receipt['Vendor Name'],
        })),
    };
}

/** Returns where the item's stock sits in the kiosk's warehouse: the reserved location, and every other one. */
async function itemLocations(inventoryId) {
    const rows = await runQuery(files.locationsQuery, { 'Inventory ID': inventoryId, Warehouse: settings.warehouse });
    const locations = rows.map(row => ({ location: row['Location'].trim(), qtyOnHand: row['Qty. On Hand'] }));
    const reserved = locations.find(entry => entry.location === settings.reserveLocation);

    return {
        warehouse: settings.warehouse,
        reserveLocation: settings.reserveLocation,
        reserved: reserved?.qtyOnHand ?? 0,
        locations: locations.filter(entry => entry !== reserved && entry.qtyOnHand > 0),
    };
}

/** Returns the item's quantity on hand in every location of one warehouse that holds some. */
async function warehouseLocations(inventoryId, warehouse) {
    const rows = await runQuery(files.locationsQuery, { 'Inventory ID': inventoryId, Warehouse: warehouse });
    return {
        warehouse,
        locations: rows.map(row => ({ location: row['Location'].trim(), qtyOnHand: row['Qty. On Hand'] })),
    };
}

/** Moves units of the item to the reserved location with a one-step transfer that the batch saves and releases. */
async function reserveUnits(inventoryId, { quantity, fromLocation }) {
    // Every run is a new transfer. The screen disables its button while a transfer runs, so one tap is one transfer.
    const document = await velixo([
        'command', 'run',
        '--batch', files.reserveBatch,
        '--param', `Warehouse=${settings.warehouse}`,
        '--param', `InventoryID=${inventoryId}`,
        '--param', `FromLocation=${fromLocation}`,
        '--param', `ToLocation=${settings.reserveLocation}`,
        '--param', `Quantity=${quantity}`,
        '--param', `Description=Inventory checker: reserve ${quantity} ${inventoryId}`,
        '--yes',
    ], settings.reserveIntent);

    const { savedKey, record } = document.result?.feedback ?? {};
    return { transfer: savedKey?.RefNbr, status: record?.transfer?.Status };
}

async function readJson(request) {
    let text = '';
    for await (const chunk of request) {
        text += chunk;
        if (text.length > 1024) {
            throw new SyntaxError('body too large');
        }
    }
    return JSON.parse(text);
}

/** Acumatica stores the item image as "Stock Items (ITEM)\file.jpg": the attachment's name is the part after the backslash. */
function imageFileOf(imageUrl) {
    if (!imageUrl) {
        return undefined;
    }

    return imageUrl.split('\\').pop();
}

/**
 * Returns the path of the item's image on disk, downloading it with the image batch the first time.
 * Images rarely change, so the cache turns every later scan of the item into a local read.
 */
async function itemImagePath(inventoryId, imageFile) {
    const folder = join(settings.imageCache, Buffer.from(inventoryId).toString('base64url'));
    const cached = join(folder, imageFile);
    if (await exists(cached)) {
        return cached;
    }

    await mkdir(folder, { recursive: true });

    await velixo([
        'command', 'run',
        '--batch', files.imageBatch,
        '--param', `InventoryID=${inventoryId}`,
        '--param', `ImageFile=${imageFile}`,
        '--download-dir', folder,
    ]);

    return (await exists(cached)) ? cached : undefined;
}

async function exists(path) {
    try {
        return (await stat(path)).isFile();
    } catch {
        return false;
    }
}

function sendJson(response, status, body) {
    response.writeHead(status, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
    response.end(JSON.stringify(body));
}

function sendFile(response, path, cacheControl) {
    response.writeHead(200, {
        'content-type': contentTypes[extname(path).toLowerCase()] ?? 'application/octet-stream',
        'cache-control': cacheControl,
    });
    createReadStream(path).pipe(response);
}

function sendVelixoError(response, error) {
    const signedOut = ['NOT_SIGNED_IN', 'SESSION_ENDED', 'ACCESS_DENIED'].includes(error.code);
    console.error(`velixo failed: ${error.code ?? 'unknown'} ${error.message}`);
    sendJson(response, signedOut ? 401 : 502, {
        error: error.code ?? 'CLI_FAILED',
        message: signedOut
            ? 'The kiosk is signed out of Velixo. Run "velixo login" on the kiosk computer.'
            : 'The ERP could not be reached. Try again in a moment.',
    });
}

/** Decodes a path segment, or returns '' for malformed percent-encoding, which the callers then refuse. */
function decodeOrEmpty(segment) {
    try {
        return decodeURIComponent(segment).trim();
    } catch {
        return '';
    }
}

async function handle(request, response) {
    const url = new URL(request.url, 'http://kiosk');
    const itemRoute = url.pathname.match(/^\/api\/items\/([^/]+)(?:\/(image|locations|reserve))?$/);
    const warehouseRoute = url.pathname.match(/^\/api\/items\/([^/]+)\/warehouses\/([^/]+)\/locations$/);

    if (url.pathname === '/api/config' && request.method === 'GET') {
        return sendJson(response, 200, {
            reserve: settings.warehouse ? { warehouse: settings.warehouse, location: settings.reserveLocation } : null,
        });
    }

    if (warehouseRoute && request.method === 'GET') {
        const inventoryId = decodeOrEmpty(warehouseRoute[1]);
        const warehouse = decodeOrEmpty(warehouseRoute[2]);
        if (!inventoryIdPattern.test(inventoryId) || !inventoryIdPattern.test(warehouse)) {
            return sendJson(response, 400, { error: 'INVALID_ID', message: 'That is not an inventory ID and a warehouse.' });
        }

        try {
            return sendJson(response, 200, await warehouseLocations(inventoryId, warehouse));
        } catch (error) {
            return sendVelixoError(response, error);
        }
    }

    if (itemRoute && (itemRoute[2] === 'locations' || itemRoute[2] === 'reserve') && !settings.warehouse) {
        return sendJson(response, 404, { error: 'RESERVE_DISABLED', message: 'Set KIOSK_WAREHOUSE to reserve units.' });
    }

    if (itemRoute && itemRoute[2] === 'reserve' && request.method === 'POST') {
        const inventoryId = decodeOrEmpty(itemRoute[1]);
        let body;
        try {
            body = await readJson(request);
        } catch {
            return sendJson(response, 400, { error: 'INVALID_BODY' });
        }

        const quantity = body?.quantity;
        const fromLocation = typeof body?.fromLocation === 'string' ? body.fromLocation.trim() : '';
        if (!inventoryIdPattern.test(inventoryId) || !Number.isInteger(quantity) || quantity < 1 || quantity > 100_000
            || !inventoryIdPattern.test(fromLocation) || fromLocation.toUpperCase() === settings.reserveLocation.toUpperCase()) {
            return sendJson(response, 400, { error: 'INVALID_REQUEST', message: 'Give a whole quantity and a source location.' });
        }

        try {
            const transfer = await reserveUnits(inventoryId, { quantity, fromLocation });
            console.log('reserve released');
            return sendJson(response, 200, transfer);
        } catch (error) {
            if (error.exitCode === 5 || error.exitCode === 6) {
                console.error(`reserve failed in the ERP: ${error.code} ${error.message}`);
                return sendJson(response, 422, { error: error.code, message: 'The ERP refused the transfer. Ask a manager to check it.' });
            }

            if (['NOT_SIGNED_IN', 'SESSION_ENDED', 'ACCESS_DENIED', 'CLI_NOT_FOUND'].includes(error.code)) {
                return sendVelixoError(response, error);
            }

            // A write that failed this way may still have run in the ERP, and a second run is a second transfer,
            // so the screen must not invite a second tap.
            console.error(`reserve outcome unknown: ${error.code ?? 'unknown'} ${error.message}`);
            return sendJson(response, 502, {
                error: error.code ?? 'CLI_FAILED',
                message: 'The transfer may have gone through. Check the reserved quantity before you try again.',
            });
        }
    }

    if (itemRoute && request.method === 'GET' && itemRoute[2] !== 'reserve') {
        const inventoryId = decodeOrEmpty(itemRoute[1]);
        if (!inventoryIdPattern.test(inventoryId)) {
            return sendJson(response, 400, { error: 'INVALID_ID', message: 'That is not an inventory ID.' });
        }

        try {
            if (itemRoute[2] === 'locations') {
                return sendJson(response, 200, await itemLocations(inventoryId));
            }

            if (itemRoute[2] === 'image') {
                const imageFile = url.searchParams.get('file') ?? '';
                // The name goes to DownloadAttachments as a pattern, so a wildcard or a ';' would download other files.
                if (!imageFile || /[\\/*?[\];]/.test(imageFile)) {
                    return sendJson(response, 400, { error: 'INVALID_FILE' });
                }

                const path = await itemImagePath(inventoryId, imageFile);
                return path ? sendFile(response, path, 'max-age=3600') : sendJson(response, 404, { error: 'NO_IMAGE' });
            }

            const started = Date.now();
            const item = await lookUpItem(inventoryId);
            console.log(`lookup ${item ? 'found' : 'not found'} in ${Date.now() - started} ms`);
            return item
                ? sendJson(response, 200, item)
                : sendJson(response, 404, { error: 'NOT_FOUND', message: 'No stock item has that inventory ID.' });
        } catch (error) {
            return sendVelixoError(response, error);
        }
    }

    if (request.method !== 'GET') {
        return sendJson(response, 405, { error: 'METHOD_NOT_ALLOWED' });
    }

    const relative = url.pathname === '/' ? 'index.html' : decodeOrEmpty(url.pathname.slice(1));
    const path = resolve(files.publicDir, relative);
    if (!path.startsWith(files.publicDir + sep) || !(await exists(path))) {
        return sendJson(response, 404, { error: 'NOT_FOUND' });
    }

    return sendFile(response, path, 'no-cache');
}

/** Accepts any user name with the kiosk's password, so the browser's own sign-in box is the whole login. */
function signedIn(request) {
    const [scheme, encoded] = (request.headers.authorization ?? '').split(' ');
    const password = scheme === 'Basic' ? Buffer.from(encoded ?? '', 'base64').toString().split(':').slice(1).join(':') : '';
    const given = Buffer.from(password);
    const expected = Buffer.from(settings.password);
    return given.length === expected.length && timingSafeEqual(given, expected);
}

// Phones allow the camera and the service worker only on a secure page, so serving them needs HTTPS.
const tls = settings.tlsCert && settings.tlsKey
    ? { cert: await readFile(settings.tlsCert), key: await readFile(settings.tlsKey) }
    : undefined;

const listener = (request, response) => {
    if (settings.password && !signedIn(request)) {
        response.writeHead(401, { 'WWW-Authenticate': 'Basic realm="Inventory kiosk"' });
        return response.end();
    }
    handle(request, response).catch(error => {
        console.error(error);
        if (!response.headersSent) {
            sendJson(response, 500, { error: 'INTERNAL' });
        }
    });
};

const server = (tls ? createSecureServer(tls, listener) : createServer(listener)).listen(settings.port, settings.host, () => {
    console.log(`Inventory kiosk on ${tls ? 'https' : 'http'}://${settings.host}:${server.address().port}`);
    console.log(`Connection: ${settings.connection ?? "the profile's default"}`);
});
