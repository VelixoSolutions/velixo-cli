// Runs the server against the real Velixo CLI and an Acumatica connection. It only reads.
// Skipped unless INVENTORYCHECKER_LIVE_CONNECTION and INVENTORYCHECKER_LIVE_ITEM are set:
//   INVENTORYCHECKER_LIVE_CONNECTION=my-acumatica INVENTORYCHECKER_LIVE_ITEM=AACOMPUT01 npm run test:live

import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const connection = process.env.INVENTORYCHECKER_LIVE_CONNECTION;
const item = process.env.INVENTORYCHECKER_LIVE_ITEM;
const skip = !connection || !item ? 'set INVENTORYCHECKER_LIVE_CONNECTION and INVENTORYCHECKER_LIVE_ITEM' : false;

let child;
let baseUrl;
let cache;

before(async () => {
    if (skip) return;
    cache = mkdtempSync(join(tmpdir(), 'inventorychecker-live-'));
    child = spawn(process.execPath, [fileURLToPath(new URL('../server.mjs', import.meta.url))], {
        env: { ...process.env, PORT: '0', VELIXO_CONNECTION: connection, KIOSK_IMAGE_CACHE: cache },
        stdio: ['ignore', 'pipe', 'inherit'],
    });
    baseUrl = await new Promise(resolve => {
        child.stdout.on('data', chunk => {
            const match = String(chunk).match(/http:\/\/[\d.]+:\d+/);
            if (match) resolve(match[0]);
        });
    });
});

after(() => {
    child?.kill();
    if (cache) rmSync(cache, { recursive: true, force: true });
});

test('looks up a real item, its deliveries and its image', { skip, timeout: 120_000 }, async () => {
    const response = await fetch(`${baseUrl}/api/items/${encodeURIComponent(item)}`);
    assert.equal(response.status, 200);

    const body = await response.json();
    assert.equal(body.inventoryId, item.trim().toUpperCase());
    assert.equal(typeof body.description, 'string');
    assert.equal(typeof body.qtyOnHand, 'number');
    assert.ok(Array.isArray(body.onOrder));

    if (body.image) {
        const image = await fetch(`${baseUrl}${body.image}`);
        assert.equal(image.status, 200);
        assert.match(image.headers.get('content-type'), /^image\//);
    }
});

test('answers 404 for an item that does not exist', { skip, timeout: 120_000 }, async () => {
    const response = await fetch(`${baseUrl}/api/items/NO-SUCH-ITEM-0000`);
    assert.equal(response.status, 404);
});
