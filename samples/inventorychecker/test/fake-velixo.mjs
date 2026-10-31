#!/usr/bin/env node
// Stands in for the `velixo` CLI in the tests. It answers from the JSON that the real CLI printed against an
// Acumatica demo tenant, and logs every call with its start and end time to FAKE_VELIXO_LOG.

import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const fixtures = fileURLToPath(new URL('fixtures/', import.meta.url));
const args = process.argv.slice(2);
const started = Date.now();

const flag = name => {
    const index = args.indexOf(name);
    return index === -1 ? undefined : args[index + 1];
};

const params = Object.fromEntries(
    args.flatMap((arg, index) => (arg === '--param' ? [args[index + 1].split('=')] : []))
);

function finish(document, exitCode = 0) {
    setTimeout(() => {
        process.stdout.write(JSON.stringify(document));
        if (process.env.FAKE_VELIXO_LOG) {
            appendFileSync(process.env.FAKE_VELIXO_LOG, JSON.stringify({ args, started, ended: Date.now() }) + '\n');
        }
        process.exit(exitCode);
    }, Number(process.env.FAKE_VELIXO_DELAY_MS ?? 40));
}

const fixture = name => JSON.parse(readFileSync(join(fixtures, name), 'utf8'));
const inventoryId = params['Inventory ID'] ?? params.InventoryID;

if (inventoryId === 'SIGNEDOUT') {
    finish({ error: { code: 'NOT_SIGNED_IN', exitCode: 4, message: 'Not signed in. Run velixo login.' } }, 4);
} else if (args[0] === 'query' && flag('--query')?.endsWith('item.vql')) {
    finish(fixture(inventoryId === 'AACOMPUT01' || inventoryId === 'CONBABY1' ? 'item-found.json' : 'item-not-found.json'));
} else if (args[0] === 'query' && flag('--query')?.endsWith('on-order.vql')) {
    finish(fixture(inventoryId === 'AACOMPUT01' ? 'on-order.json' : 'on-order-none.json'));
} else if (args[0] === 'query' && flag('--query')?.endsWith('warehouses.vql')) {
    finish(fixture(inventoryId === 'AACOMPUT01' ? 'warehouses.json' : 'item-not-found.json'));
} else if (args[0] === 'query' && flag('--query')?.endsWith('locations.vql')) {
    finish(fixture('locations.json'));
} else if (args[0] === 'command' && flag('--batch')?.endsWith('reserve-transfer.batch.json')) {
    if (params.Quantity === '8') {
        finish({ status: 'done', result: {} });
    } else if (params.Quantity === '5') {
        finish({ error: { code: 'UNEXPECTED', exitCode: 1, message: 'The gateway timed out.' } }, 1);
    } else if (params.Quantity === '9') {
        finish({ error: { code: 'ERP_ACTION_REFUSED', exitCode: 6, message: 'The Release button is disabled.' } }, 6);
    } else {
        finish(fixture('reserve-transfer.json'));
    }
} else if (args[0] === 'command' && args[1] === 'run') {
    writeFileSync(join(flag('--download-dir'), params.ImageFile), 'fake image bytes');
    finish({ status: 'done', result: { attachments: [{ fileName: params.ImageFile }] } });
} else {
    finish({ error: { code: 'USAGE', exitCode: 2, message: `The fake CLI does not know: ${args.join(' ')}` } }, 2);
}
