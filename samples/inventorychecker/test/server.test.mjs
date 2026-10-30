// Tests the server against a fake `velixo`, so they need no ERP and no sign-in: node --test test/

import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { chmodSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, beforeEach, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const sampleDir = fileURLToPath(new URL('..', import.meta.url));
const fakeVelixo = fileURLToPath(new URL('fake-velixo.mjs', import.meta.url));

let work;
let log;
let server;
let baseUrl;

async function startServer(extraEnv = {}) {
    const child = spawn(process.execPath, [join(sampleDir, 'server.mjs')], {
        env: {
            ...process.env,
            PORT: '0',
            HOST: '127.0.0.1',
            VELIXO_BIN: fakeVelixo,
            VELIXO_CONNECTION: 'test-acumatica',
            KIOSK_WAREHOUSE: 'RETAIL',
            KIOSK_IMAGE_CACHE: join(work, 'images'),
            FAKE_VELIXO_LOG: log,
            ...extraEnv,
        },
        stdio: ['ignore', 'pipe', 'pipe'],
    });

    const url = await new Promise((resolve, reject) => {
        child.stdout.on('data', chunk => {
            const match = String(chunk).match(/http:\/\/[\d.]+:\d+/);
            if (match) resolve(match[0]);
        });
        child.on('exit', code => reject(new Error(`server exited with ${code}`)));
    });

    return { child, url };
}

function cliCalls() {
    try {
        return readFileSync(log, 'utf8').trim().split('\n').filter(Boolean).map(line => JSON.parse(line));
    } catch {
        return [];
    }
}

before(async () => {
    chmodSync(fakeVelixo, 0o755);
    work = mkdtempSync(join(tmpdir(), 'inventorychecker-'));
    log = join(work, 'calls.log');
    server = await startServer();
    baseUrl = server.url;
});

after(() => {
    server.child.kill();
    rmSync(work, { recursive: true, force: true });
});

beforeEach(() => writeFileSync(log, ''));

test('returns the item, its quantities and its next deliveries', async () => {
    const response = await fetch(`${baseUrl}/api/items/AACOMPUT01`);

    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), {
        inventoryId: 'AACOMPUT01',
        description: 'Acer Laptop Computer',
        baseUnit: 'EA',
        qtyOnHand: 643,
        qtyAvailable: 653,
        image: '/api/items/AACOMPUT01/image?file=AACOMPUT01.jpg',
        warehouses: [
            { warehouse: 'RETAIL', name: 'Retail Warehouse', qtyOnHand: 435, qtyAvailable: 433, isThisStore: true },
            { warehouse: 'WHOLESALE', name: 'Wholesale Warehouse', qtyOnHand: 208, qtyAvailable: 218, isThisStore: false },
        ],
        onOrder: [{ orderNbr: 'PO002768', promisedDate: '2026-10-22T00:00:00', quantity: 1000, vendor: 'Goto Vendor Inc' }],
    });
});

test('runs every query with the scanned ID, the connection and an intent, never prompting', async () => {
    await fetch(`${baseUrl}/api/items/AACOMPUT01`);

    const calls = cliCalls().sort((a, b) => a.started - b.started).map(call => call.args);
    assert.equal(calls.length, 3);
    for (const args of calls) {
        assert.deepEqual(args.slice(0, 2), ['query', 'run']);
        assert.ok(args.includes('Inventory ID=AACOMPUT01'));
        assert.equal(args[args.indexOf('--connection') + 1], 'test-acumatica');
        assert.equal(args[args.indexOf('--output') + 1], 'json');
        assert.ok(args.includes('--non-interactive'));
        assert.ok(args[args.indexOf('--intent') + 1].length > 0);
    }
    const queries = calls.map(args => args[args.indexOf('--query') + 1].split('/').pop());
    assert.equal(queries[0], 'item.vql');
    assert.deepEqual(queries.slice(1).sort(), ['on-order.vql', 'warehouses.vql']);
});

test('returns an empty delivery list for an item with nothing on order', async () => {
    const response = await fetch(`${baseUrl}/api/items/CONBABY1`);

    assert.equal(response.status, 200);
    assert.deepEqual((await response.json()).onOrder, []);
});

test('answers 404 for an unknown item, without running the on-order query', async () => {
    const response = await fetch(`${baseUrl}/api/items/NOSUCHITEM`);

    assert.equal(response.status, 404);
    assert.equal((await response.json()).error, 'NOT_FOUND');
    assert.equal(cliCalls().length, 1);
});

test('refuses a code that cannot be an inventory ID, without calling the CLI', async () => {
    for (const code of ['bad;id', '--help', 'A'.repeat(31)]) {
        const response = await fetch(`${baseUrl}/api/items/${encodeURIComponent(code)}`);
        assert.equal(response.status, 400, code);
    }
    assert.equal(cliCalls().length, 0);
});

test('tells the screen to sign in again when the CLI is signed out', async () => {
    const response = await fetch(`${baseUrl}/api/items/SIGNEDOUT`);

    assert.equal(response.status, 401);
    assert.match((await response.json()).message, /velixo login/);
});

test('downloads an image once, then serves it from the cache', async () => {
    const url = `${baseUrl}/api/items/AACOMPUT01/image?file=AACOMPUT01.jpg`;

    const first = await fetch(url);
    assert.equal(first.status, 200);
    assert.equal(first.headers.get('content-type'), 'image/jpeg');
    assert.equal(await first.text(), 'fake image bytes');

    const [download] = cliCalls().map(call => call.args);
    assert.deepEqual(download.slice(0, 2), ['command', 'run']);
    assert.ok(download.includes('InventoryID=AACOMPUT01'));
    assert.ok(download.includes('ImageFile=AACOMPUT01.jpg'));
    assert.ok(download.includes('--yes'));
    assert.ok(download.includes('--force'));

    const second = await fetch(url);
    assert.equal(second.status, 200);
    assert.equal(cliCalls().length, 1);
});

test('refuses an image file name that points outside the item folder', async () => {
    for (const file of ['../server.mjs', '..\\server.mjs', '']) {
        const response = await fetch(`${baseUrl}/api/items/AACOMPUT01/image?file=${encodeURIComponent(file)}`);
        assert.equal(response.status, 400, file);
    }
    assert.equal(cliCalls().length, 0);
});

test('reads the deliveries and the warehouses at the same time, once the item is found', async () => {
    await fetch(`${baseUrl}/api/items/AACOMPUT01`);

    const [item, ...rest] = cliCalls().sort((a, b) => a.started - b.started);
    assert.match(item.args[item.args.indexOf('--query') + 1], /item\.vql$/);
    assert.equal(rest.length, 2);
    for (const call of rest) {
        assert.ok(call.started >= item.ended, 'a follow-up query started before the item was found');
    }
    assert.ok(rest[1].started < rest[0].ended, 'the follow-up queries ran one after the other');
});

test('answers several screens that scan at once', async () => {
    const responses = await Promise.all(
        ['AACOMPUT01', 'CONBABY1', 'NOSUCHITEM', 'AACOMPUT01'].map(id => fetch(`${baseUrl}/api/items/${id}`))
    );
    assert.deepEqual(responses.map(response => response.status), [200, 200, 404, 200]);
});

test('serves the kiosk page and nothing outside its folder', async () => {
    const page = await fetch(`${baseUrl}/`);
    assert.equal(page.status, 200);
    assert.match(await page.text(), /<title>Inventory Check<\/title>/);

    for (const path of ['/../server.mjs', '/%2e%2e/server.mjs', '/velixo/item.vql']) {
        const response = await fetch(`${baseUrl}${path}`);
        assert.equal(response.status, 404, path);
    }
});

test('reports the ERP as unreachable when the CLI cannot be started', async () => {
    const broken = await startServer({ VELIXO_BIN: join(work, 'no-such-velixo') });
    try {
        const response = await fetch(`${broken.url}/api/items/AACOMPUT01`);
        assert.equal(response.status, 502);
    } finally {
        broken.child.kill();
    }
});

const reserve = (body, id = 'AACOMPUT01') =>
    fetch(`${baseUrl}/api/items/${id}/reserve`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(body),
    });

test('tells the screen where units can be reserved', async () => {
    const response = await fetch(`${baseUrl}/api/config`);
    assert.deepEqual(await response.json(), { reserve: { warehouse: 'RETAIL', location: 'RESERVED' } });
});

test('lists the stock by location, apart from what is already reserved', async () => {
    const response = await fetch(`${baseUrl}/api/items/AACOMPUT01/locations`);

    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), {
        warehouse: 'RETAIL',
        reserveLocation: 'RESERVED',
        reserved: 1,
        locations: [{ location: 'STORAGE', qtyOnHand: 434 }],
    });
    const [args] = cliCalls().map(call => call.args);
    assert.ok(args.includes('Warehouse=RETAIL'));
});

test('reserves units with a transfer from the chosen location to the reserved one', async () => {
    const response = await reserve({ quantity: 3, fromLocation: 'STORAGE' });

    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { transfer: '002934', status: 'Released' });

    const transfer = cliCalls().map(call => call.args).find(args => args[0] === 'command');
    for (const param of ['Warehouse=RETAIL', 'InventoryID=AACOMPUT01', 'FromLocation=STORAGE', 'ToLocation=RESERVED', 'Quantity=3']) {
        assert.ok(transfer.includes(param), param);
    }
    assert.ok(transfer.includes('--yes'));
    assert.ok(transfer.includes('--force'), 'reserving the same units twice is two transfers');
    assert.match(transfer[transfer.indexOf('--intent') + 1], /reserved location/);
});

test('runs two reservations of the same item one after the other, each with its own stock check', async () => {
    const responses = await Promise.all([
        reserve({ quantity: 2, fromLocation: 'STORAGE' }),
        reserve({ quantity: 3, fromLocation: 'STORAGE' }),
    ]);
    assert.deepEqual(responses.map(response => response.status), [200, 200]);

    const calls = cliCalls().sort((a, b) => a.started - b.started);
    assert.deepEqual(
        calls.map(call => (call.args[0] === 'command' ? 'transfer' : 'stock check')),
        ['stock check', 'transfer', 'stock check', 'transfer']
    );
    assert.ok(calls[2].started >= calls[1].ended, 'the second stock check ran before the first transfer ended');
});

test('refuses to reserve more than the source location holds, without writing', async () => {
    const response = await reserve({ quantity: 435, fromLocation: 'STORAGE' });

    assert.equal(response.status, 400);
    assert.equal((await response.json()).error, 'NOT_ENOUGH_STOCK');
    assert.ok(cliCalls().every(call => call.args[0] !== 'command'));
});

test('refuses a reservation from a location without stock, or from the reserved location itself', async () => {
    for (const fromLocation of ['R10RACK1', 'RESERVED']) {
        const response = await reserve({ quantity: 1, fromLocation });
        assert.equal(response.status, 400, fromLocation);
    }
    assert.ok(cliCalls().every(call => call.args[0] !== 'command'));
});

test('refuses a quantity that is not a whole number of at least one', async () => {
    for (const quantity of [0, -2, 1.5, '3', null]) {
        const response = await reserve({ quantity, fromLocation: 'STORAGE' });
        assert.equal(response.status, 400, String(quantity));
    }
    const garbage = await fetch(`${baseUrl}/api/items/AACOMPUT01/reserve`, { method: 'POST', body: 'not json' });
    assert.equal(garbage.status, 400);
    assert.equal(cliCalls().length, 0);
});

test('answers 422 when the ERP refuses the transfer', async () => {
    const response = await reserve({ quantity: 9, fromLocation: 'STORAGE' });
    assert.equal(response.status, 422);
});

test('turns reservations off when no warehouse is configured', async () => {
    const plain = await startServer({ KIOSK_WAREHOUSE: '' });
    try {
        assert.deepEqual(await (await fetch(`${plain.url}/api/config`)).json(), { reserve: null });
        assert.equal((await fetch(`${plain.url}/api/items/AACOMPUT01/locations`)).status, 404);
    } finally {
        plain.child.kill();
    }
});
