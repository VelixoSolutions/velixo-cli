// The kiosk's screen: it reads a barcode scanner as keystrokes, asks the server for the item and shows it.

const resetAfterSeconds = 30;
const lowStockThreshold = 5;

const $ = id => document.getElementById(id);
const locale = navigator.language;
const quantityFormat = new Intl.NumberFormat(locale, {
    maximumFractionDigits: 2,
});
const dateFormat = new Intl.DateTimeFormat(locale, {
    weekday: 'short',
    month: 'long',
    day: 'numeric',
});
const clockFormat = new Intl.DateTimeFormat(locale, {
    hour: '2-digit',
    minute: '2-digit',
});

let resetTimer;
let currentItem;
let reserveConfig;
let lookup = 0;

function setState(state) {
    document.body.dataset.state = state;
}

function goIdle() {
    clearTimeout(resetTimer);
    $('scan-title').textContent = 'Scan an item';
    $('scan-subtitle').textContent = 'Hold the barcode under the scanner';
    setState('idle');
}

function scheduleReset() {
    clearTimeout(resetTimer);
    document.body.style.setProperty('--reset-after', `${resetAfterSeconds}s`);
    resetTimer = setTimeout(goIdle, resetAfterSeconds * 1000);
}

async function search(code) {
    const inventoryId = code.trim().toUpperCase();
    if (!inventoryId) {
        return;
    }

    const current = ++lookup;
    clearTimeout(resetTimer);
    $('scan-title').innerHTML = '';
    $('scan-title').append(
        'Searching ',
        Object.assign(document.createElement('code'), {
            textContent: inventoryId,
        })
    );
    $('scan-subtitle').textContent = 'Checking stock and incoming orders';
    setState('searching');

    let response;
    let body;
    try {
        response = await fetch(`/api/items/${encodeURIComponent(inventoryId)}`);
        body = await response.json();
    } catch {
        response = undefined;
    }

    if (current !== lookup) {
        return;
    }

    if (response?.ok) {
        showItem(body);
    } else if (response?.status === 404) {
        showMessage({
            kind: 'warning',
            icon: '?',
            title: ['No item matches ', inventoryId],
            text: 'Check the label, or ask a team member for help.',
        });
    } else if (response?.status === 400) {
        showMessage({
            kind: 'warning',
            icon: '!',
            title: ['That code is not an item'],
            text: 'Scan the item barcode, or type its inventory ID.',
        });
    } else {
        showMessage({
            kind: 'error',
            icon: '!',
            title: ['Lookup unavailable'],
            text:
                body?.message ??
                'The kiosk cannot reach the server. Try again in a moment.',
        });
    }
}

function showItem(item) {
    stopCounting($('qty-on-hand'));
    stopCounting($('qty-available'));
    currentItem = item;
    $('reserve-open').hidden = !reserveConfig;
    $('item-id').textContent = item.inventoryId;
    $('item-description').textContent = item.description;
    $('qty-on-hand').textContent = quantityFormat.format(item.qtyOnHand);
    $('qty-available').textContent = quantityFormat.format(item.qtyAvailable);
    $('unit-on-hand').textContent = item.baseUnit;
    $('unit-available').textContent = item.baseUnit;

    showStockPill(item.qtyAvailable);
    showImage(item.image);
    showWarehouses(item.warehouses);
    showIncoming(item.onOrder);
    setState('result');
    scheduleReset();
}

function showStockPill(qtyAvailable) {
    const pill = $('stock-pill');
    if (qtyAvailable <= 0) {
        pill.className = 'stock-pill out';
        pill.textContent = 'Out of stock';
    } else if (qtyAvailable <= lowStockThreshold) {
        pill.className = 'stock-pill low';
        pill.textContent = 'Low stock';
    } else {
        pill.className = 'stock-pill in';
        pill.textContent = 'In stock';
    }
}

function showImage(source) {
    const holo = document.querySelector('.holo');
    const image = $('item-image');
    image.classList.remove('loaded');
    holo.classList.remove('has-image');
    image.removeAttribute('src');

    if (!source) {
        holo.classList.remove('loading-image');
        return;
    }

    // The first scan of an item downloads its image from the ERP, which takes a few seconds.
    holo.classList.add('loading-image');
    image.onload = () => {
        holo.classList.remove('loading-image');
        holo.classList.add('has-image');
        image.classList.add('loaded');
    };
    image.onerror = () => holo.classList.remove('loading-image');
    image.src = source;
}

function showWarehouses(warehouses) {
    const largest = Math.max(1, ...warehouses.map(entry => entry.qtyOnHand));
    $('warehouse-list').replaceChildren(
        ...warehouses.map((entry, index) => {
            const row = document.createElement('li');
            row.classList.toggle('this-store', entry.isThisStore);
            row.style.setProperty('--share', String(Math.max(0, entry.qtyOnHand) / largest));
            row.style.setProperty('--delay', `${index * 90}ms`);

            const name = document.createElement('span');
            name.className = 'warehouse-name';
            name.append(
                Object.assign(document.createElement('b'), { textContent: entry.name || entry.warehouse }),
                Object.assign(document.createElement('code'), { textContent: entry.warehouse })
            );
            if (entry.isThisStore) {
                name.append(Object.assign(document.createElement('em'), { textContent: 'This store' }));
            }

            const bar = Object.assign(document.createElement('span'), { className: 'warehouse-bar' });
            const onHand = Object.assign(document.createElement('span'), { className: 'warehouse-qty', textContent: quantityFormat.format(entry.qtyOnHand) });
            const available = Object.assign(document.createElement('span'), {
                className: `warehouse-qty ${entry.qtyAvailable <= 0 ? 'none' : ''}`,
                textContent: quantityFormat.format(entry.qtyAvailable),
            });
            available.dataset.warehouse = entry.warehouse;
            row.append(name, bar, onHand, available);
            return row;
        })
    );

    if (!warehouses.length) {
        $('warehouse-list').append(Object.assign(document.createElement('li'), { className: 'empty', textContent: 'Not stocked in any warehouse' }));
    }
}

function showIncoming(receipts) {
    const panel = $('incoming');
    const more = $('more-receipts');
    more.replaceChildren();

    if (!receipts.length) {
        panel.classList.add('none');
        $('next-date').textContent = 'Nothing on order';
        $('next-when').textContent = '';
        $('next-meta').textContent = 'No open purchase order for this item.';
        return;
    }

    panel.classList.remove('none');
    const [next, ...later] = receipts;
    const when = relativeDay(next.promisedDate);

    $('next-date').textContent = next.promisedDate
        ? dateFormat.format(parseDate(next.promisedDate))
        : 'Date to be confirmed';
    $('next-when').textContent = when.text;
    $('next-when').classList.toggle('overdue', when.overdue);

    const meta = $('next-meta');
    meta.replaceChildren(
        bold(quantityFormat.format(next.quantity)),
        ' expected on ',
        bold(next.orderNbr),
        next.vendor ? ` from ${next.vendor}` : ''
    );

    for (const receipt of later.slice(0, 3)) {
        const line = document.createElement('li');
        const date = receipt.promisedDate
            ? dateFormat.format(parseDate(receipt.promisedDate))
            : 'Date to be confirmed';
        line.append(
            document.createTextNode(`${date} · ${receipt.orderNbr}`),
            bold(quantityFormat.format(receipt.quantity))
        );
        more.append(line);
    }
}

function bold(text) {
    return Object.assign(document.createElement('b'), { textContent: text });
}

/** The ERP sends dates as local midnight without a zone, so read them as local dates. */
function parseDate(value) {
    const [year, month, day] = value.slice(0, 10).split('-').map(Number);
    return new Date(year, month - 1, day);
}

function relativeDay(value) {
    if (!value) {
        return { text: '', overdue: false };
    }

    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const days = Math.round((parseDate(value) - today) / 86_400_000);

    if (days === 0) return { text: 'today', overdue: false };
    if (days === 1) return { text: 'tomorrow', overdue: false };
    if (days > 1) return { text: `in ${days} days`, overdue: false };
    return {
        text: `late by ${-days} day${days === -1 ? '' : 's'}`,
        overdue: true,
    };
}

function showMessage({ kind, icon, title, text }) {
    const [prefix, code] = title;
    $('message-icon').textContent = icon;
    $('message-icon').className =
        `message-icon ${kind === 'error' ? 'error' : ''}`;
    $('message-title').replaceChildren(
        prefix,
        ...(code
            ? [
                  Object.assign(document.createElement('code'), {
                      textContent: code,
                  }),
              ]
            : [])
    );
    $('message-text').textContent = text;
    setState('message');
    clearTimeout(resetTimer);
    resetTimer = setTimeout(goIdle, 8000);
}

// A barcode scanner types the code and presses Enter. Collect the keys anywhere on the page.
let buffer = '';
let bufferTimer;

document.addEventListener('keydown', event => {
    if (!$('keypad').hidden || event.metaKey || event.ctrlKey || event.altKey) {
        return;
    }

    if (!$('reserve').hidden) {
        if (event.key === 'Escape') {
            closeReserve();
        }
        return;
    }

    if (event.key === 'Enter') {
        event.preventDefault();
        const code = buffer;
        buffer = '';
        search(code);
    } else if (event.key === 'Escape') {
        buffer = '';
        goIdle();
    } else if (event.key.length === 1) {
        buffer += event.key;
        clearTimeout(bufferTimer);
        bufferTimer = setTimeout(() => (buffer = ''), 1500);
    }
});

// On a touch screen without a keyboard, type the code on the keypad.
const keypadKeys = [...'1234567890QWERTYUIOPASDFGHJKL-ZXCVBNM'];
let keypadValue = '';

function renderKeypad() {
    const keys = $('keypad-keys');
    for (const key of keypadKeys) {
        keys.append(
            Object.assign(document.createElement('button'), {
                type: 'button',
                textContent: key,
                onclick: () => typeKey(key),
            })
        );
    }
    keys.append(
        Object.assign(document.createElement('button'), {
            type: 'button',
            className: 'wide',
            textContent: '⌫ Delete',
            onclick: () => typeKey('Backspace'),
        })
    );
}

function typeKey(key) {
    keypadValue =
        key === 'Backspace'
            ? keypadValue.slice(0, -1)
            : (keypadValue + key).slice(0, 30);
    $('keypad-value').textContent = keypadValue;
}

function openKeypad() {
    keypadValue = '';
    $('keypad-value').textContent = '';
    $('keypad').hidden = false;
    clearTimeout(resetTimer);
}

function closeKeypad() {
    $('keypad').hidden = true;
}

$('type-code').addEventListener('click', openKeypad);
$('go-home').addEventListener('click', goIdle);
$('keypad').addEventListener('click', event => {
    const action = event.target.dataset?.action;
    if (action === 'cancel') {
        closeKeypad();
    } else if (action === 'search') {
        closeKeypad();
        search(keypadValue);
    }
});
document.addEventListener('keydown', event => {
    if ($('keypad').hidden) {
        return;
    }

    if (event.key === 'Enter') {
        event.preventDefault();
        closeKeypad();
        search(keypadValue);
    } else if (event.key === 'Escape') {
        closeKeypad();
    } else if (event.key === 'Backspace') {
        typeKey('Backspace');
    } else if (/^[a-z0-9-]$/i.test(event.key)) {
        typeKey(event.key.toUpperCase());
    }
});

// A scanner presses Enter after every code, so a button must not keep the focus after a tap.
document.addEventListener('click', event => event.target.closest?.('button')?.blur());

// Full screen needs a tap first: browsers refuse it without one.
$('fullscreen').addEventListener('click', () =>
    document.documentElement.requestFullscreen?.()
);
document.addEventListener(
    'pointerdown',
    () => {
        if (
            !document.fullscreenElement &&
            document.body.dataset.state === 'idle' &&
            $('keypad').hidden
        ) {
            document.documentElement.requestFullscreen?.().catch(() => {});
        }
    },
    { once: true }
);

// Staff can move units to the reserved location. The server runs an inventory transfer through the CLI.
let reserveState;

async function openReserve() {
    if (!currentItem || !reserveConfig) {
        return;
    }

    clearTimeout(resetTimer);
    reserveState = { inventoryId: currentItem.inventoryId, quantity: 1, from: undefined, locations: [], busy: true };
    $('reserve-item').textContent = currentItem.inventoryId;
    $('reserve-subtitle').textContent = `Move units to ${reserveConfig.location} in ${reserveConfig.warehouse}.`;
    $('reserve-form').hidden = true;
    $('reserve-celebrate').hidden = true;
    $('reserve-celebrate').classList.remove('play');
    $('reserve-cancel').textContent = 'Cancel';
    setReserveStatus('busy', 'Finding the item in the warehouse');
    renderReserveButton();
    $('reserve').hidden = false;

    try {
        const response = await fetch(`/api/items/${encodeURIComponent(reserveState.inventoryId)}/locations`);
        const body = await response.json();
        if (!response.ok) {
            throw new Error(body.message);
        }

        reserveState.locations = body.locations;
        reserveState.reserved = body.reserved;
        reserveState.from = body.locations.find(entry => entry.qtyOnHand >= 1);
        reserveState.busy = false;

        const subtitle = $('reserve-subtitle');
        subtitle.replaceChildren(
            `Move units to ${body.reserveLocation} in ${body.warehouse}. Already reserved: `,
            bold(quantityFormat.format(body.reserved))
        );

        if (!reserveState.from) {
            setReserveStatus('failed', `No stock of this item in ${body.warehouse} to reserve.`);
        } else {
            setReserveStatus('', '');
            $('reserve-form').hidden = false;
            renderReserveLocations();
        }
    } catch (error) {
        reserveState.busy = false;
        setReserveStatus('failed', error.message || 'The warehouse could not be read. Try again.');
    }

    renderReserveButton();
}

function renderReserveLocations() {
    const chips = $('reserve-locations');
    chips.replaceChildren(
        ...reserveState.locations.map(entry => {
            const chip = document.createElement('button');
            chip.type = 'button';
            chip.setAttribute('aria-pressed', String(entry === reserveState.from));
            chip.disabled = entry.qtyOnHand < 1;
            chip.append(
                Object.assign(document.createElement('span'), { textContent: entry.location }),
                Object.assign(document.createElement('small'), { textContent: `${quantityFormat.format(entry.qtyOnHand)} on hand` })
            );
            chip.onclick = () => {
                reserveState.from = entry;
                reserveState.quantity = Math.max(1, Math.min(reserveState.quantity, Math.floor(entry.qtyOnHand)));
                renderReserveLocations();
            };
            return chip;
        })
    );
    $('reserve-quantity').textContent = reserveState.quantity;
    renderReserveButton();
}

function renderReserveButton() {
    const button = $('reserve-confirm');
    const ready = reserveState && !reserveState.busy && reserveState.from && !reserveState.done;
    button.disabled = !ready;
    button.hidden = Boolean(reserveState?.done);
    button.textContent = `Reserve ${reserveState?.quantity ?? ''} unit${reserveState?.quantity === 1 ? '' : 's'}`;
    $('reserve-cancel').disabled = Boolean(reserveState?.busy);
}

function setReserveStatus(kind, text) {
    const status = $('reserve-status');
    status.className = `sheet-status ${kind}`;
    status.textContent = text;
}

async function confirmReserve() {
    const state = reserveState;
    const { inventoryId, quantity, from } = state;
    reserveState.busy = true;
    $('reserve-form').hidden = true;
    setReserveStatus('busy', `Releasing a transfer of ${quantity} from ${from.location}`);
    renderReserveButton();

    let response;
    let body;
    try {
        response = await fetch(`/api/items/${encodeURIComponent(inventoryId)}/reserve`, {
            method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({ quantity, fromLocation: from.location }),
        });
        body = await response.json();
    } catch {
        response = undefined;
    }

    if (state !== reserveState) {
        refreshItem(inventoryId);
        return;
    }

    reserveState.busy = false;
    if (response?.status === 502) {
        reserveState.done = true;
        setReserveStatus('failed', body?.message ?? 'The transfer may have gone through. Check the reserved quantity.');
        $('reserve-cancel').textContent = 'Done';
        renderReserveButton();
        return;
    }

    if (!response?.ok) {
        $('reserve-form').hidden = false;
        setReserveStatus('failed', body?.message ?? 'The kiosk cannot reach the server. Try again.');
        renderReserveButton();
        return;
    }

    reserveState.done = true;
    setReserveStatus('', '');
    renderReserveButton();
    $('reserve-cancel').textContent = 'Done';
    reserveState.reserved += quantity;
    $('reserve-subtitle').replaceChildren(
        `Moved to ${reserveConfig.location} in ${reserveConfig.warehouse}. Reserved now: `,
        bold(quantityFormat.format(reserveState.reserved))
    );
    celebrate({ from: from.location, to: reserveConfig.location, quantity, transfer: body.transfer });
    refreshItem(inventoryId);
}

/** Plays the success animation: a crate flies into the vault, the seal draws itself and particles burst. */
function celebrate({ from, to, quantity, transfer }) {
    const panel = $('reserve-celebrate');
    $('celebrate-from').textContent = from;
    $('celebrate-to').textContent = to;
    $('celebrate-transfer').textContent = transfer;

    const colors = ['#22e6ff', '#9b6cff', '#2dffb3', '#ffffff'];
    $('celebrate-burst').replaceChildren(
        ...Array.from({ length: 28 }, (_, index) => {
            const particle = document.createElement('i');
            particle.style.setProperty('--a', `${(index / 28) * 360 + Math.random() * 10}deg`);
            particle.style.setProperty('--r', `${70 + Math.random() * 70}px`);
            particle.style.setProperty('--d', `${1250 + Math.random() * 200}ms`);
            particle.style.setProperty('--c', colors[index % colors.length]);
            return particle;
        })
    );

    panel.classList.remove('play');
    panel.hidden = false;
    void panel.offsetWidth;
    panel.classList.add('play');

    countUp($('celebrate-count'), 0, quantity, 900, 1500);
    clearTimeout(reserveState.closeTimer);
    reserveState.closeTimer = setTimeout(closeReserve, 6000);
}

function closeReserve() {
    if (reserveState?.busy) {
        return;
    }

    clearTimeout(reserveState?.closeTimer);
    $('reserve').hidden = true;
    if (document.body.dataset.state === 'result') {
        scheduleReset();
    }
}

/** Reads the item again after a reservation and counts the changed quantities to their new values. */
async function refreshItem(inventoryId) {
    const before = currentItem;
    if (before?.inventoryId !== inventoryId) {
        return;
    }

    try {
        const response = await fetch(`/api/items/${encodeURIComponent(inventoryId)}`);
        if (!response.ok || currentItem !== before) {
            return;
        }

        const item = await response.json();
        currentItem = item;
        showStockPill(item.qtyAvailable);
        for (const entry of item.warehouses) {
            const cell = document.querySelector(`.warehouse-qty[data-warehouse="${CSS.escape(entry.warehouse)}"]`);
            const previous = before.warehouses.find(old => old.warehouse === entry.warehouse);
            if (cell && previous && previous.qtyAvailable !== entry.qtyAvailable) {
                cell.classList.remove('changed');
                void cell.offsetWidth;
                cell.classList.add('changed');
                countUp(cell, previous.qtyAvailable, entry.qtyAvailable, 1200, 0);
            }
        }
        for (const [id, from, to] of [
            ['qty-on-hand', before.qtyOnHand, item.qtyOnHand],
            ['qty-available', before.qtyAvailable, item.qtyAvailable],
        ]) {
            if (from !== to) {
                const element = $(id);
                element.classList.remove('changed');
                void element.offsetWidth;
                element.classList.add('changed');
                countUp(element, from, to, 1200, 0);
            }
        }
    } catch {
        /** The numbers stay as they were; the next scan shows the new ones. */
    }
}

const countAnimations = new Map();

function stopCounting(element) {
    cancelAnimationFrame(countAnimations.get(element));
    countAnimations.delete(element);
}

function countUp(element, from, to, duration, delay) {
    stopCounting(element);
    const start = performance.now() + delay;
    element.textContent = quantityFormat.format(from);
    const step = now => {
        const progress = Math.min(1, Math.max(0, (now - start) / duration));
        const eased = 1 - Math.pow(1 - progress, 3);
        element.textContent = quantityFormat.format(progress < 1 ? Math.round(from + (to - from) * eased) : to);
        if (progress < 1) {
            countAnimations.set(element, requestAnimationFrame(step));
        } else {
            countAnimations.delete(element);
        }
    };
    countAnimations.set(element, requestAnimationFrame(step));
}

$('reserve-open').addEventListener('click', openReserve);
$('reserve-cancel').addEventListener('click', closeReserve);
$('reserve-confirm').addEventListener('click', confirmReserve);
document.querySelectorAll('.stepper button').forEach(button =>
    button.addEventListener('click', () => {
        const max = Math.max(1, Math.floor(reserveState.from?.qtyOnHand ?? 1));
        reserveState.quantity = Math.min(max, Math.max(1, reserveState.quantity + Number(button.dataset.step)));
        $('reserve-quantity').textContent = reserveState.quantity;
        renderReserveButton();
    })
);

fetch('/api/config')
    .then(response => response.json())
    .then(config => (reserveConfig = config.reserve ?? undefined))
    .catch(() => {});

function tick() {
    $('clock').textContent = clockFormat.format(new Date());
}

renderKeypad();
tick();
setInterval(tick, 10_000);
goIdle();
