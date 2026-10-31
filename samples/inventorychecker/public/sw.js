// The service worker makes the page installable and opens it without a network. It never stores stock figures:
// every lookup goes to the server, so a quantity on screen is always one the ERP just returned.

const shellCache = 'inventory-shell-v1';
const imageCache = 'inventory-images-v1';
const shell = [
    '/',
    'styles.css',
    'app.js',
    'camera.js',
    'manifest.webmanifest',
    'velixo-logo.svg',
    'velixo-symbol.svg',
    'icons/icon-192.png',
    'icons/apple-touch-icon.png',
];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(shellCache).then(cache => cache.addAll(shell)).then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(key => key !== shellCache && key !== imageCache).map(key => caches.delete(key))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', event => {
    const url = new URL(event.request.url);
    if (event.request.method !== 'GET' || url.origin !== self.location.origin) {
        return;
    }

    if (/^\/api\/items\/[^/]+\/image$/.test(url.pathname)) {
        event.respondWith(cacheFirst(event.request));
    } else if (!url.pathname.startsWith('/api/')) {
        event.respondWith(networkFirst(event.request));
    }
});

/** Item images rarely change, and the server already keeps each one after its first download. */
async function cacheFirst(request) {
    const cache = await caches.open(imageCache);
    const cached = await cache.match(request);
    if (cached) {
        return cached;
    }

    const response = await fetch(request);
    if (response.ok) {
        cache.put(request, response.clone());
    }
    return response;
}

/** The page comes from the server whenever it answers, so an update shows on the next open. */
async function networkFirst(request) {
    const cache = await caches.open(shellCache);
    try {
        const response = await fetch(request);
        if (response.ok) {
            cache.put(request, response.clone());
        }
        return response;
    } catch (error) {
        const cached = await cache.match(request, { ignoreSearch: true });
        if (cached) {
            return cached;
        }
        throw error;
    }
}
