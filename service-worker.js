// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [ /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.svg$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/ ];
const offlineAssetsExclude = [ /^service-worker\.js$/, /^(?:sitemap\.xml|robots\.txt)$/ ];

async function onInstall(event) {
    console.info('Service worker: Install');

    // Fetch and cache all matching items from the assets manifest
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
    await caches.open(cacheName).then(cache => cache.addAll(assetsRequests));
}

async function onActivate(event) {
    console.info('Service worker: Activate');

    // Delete unused caches
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    if (event.request.method !== 'GET') return fetch(event.request);
    const url = new URL(event.request.url);
    if (url.origin !== self.location.origin) return fetch(event.request);
    // Crawler files must never become a cached HTML shell or an offline 404 page.
    // Fetch from the network rather than reuse browser HTTP/Cache Storage entries.
    if (url.pathname === '/sitemap.xml' || url.pathname === '/robots.txt') {
        try { return await fetch(event.request, { cache: 'no-store' }); }
        catch {
            return new Response('This crawler file requires a network connection. Reconnect and reload.', {
                status: 503, statusText: 'Service Unavailable',
                headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-store' }
            });
        }
    }
    const cache = await caches.open(cacheName);
    if (event.request.mode === 'navigate') {
        // Only real, published HTML documents have a navigation fallback.
        // / and /index.html share the workspace; guides keep their own content.
        const documentPath = url.pathname === '/' ? 'index.html'
            : url.pathname.endsWith('/') ? url.pathname.slice(1) + 'index.html'
            : url.pathname.slice(1);
        const isDocument = documentPath.endsWith('.html') && documentPath !== '404.html'
            && self.assetsManifest.assets.some(asset => asset.url === documentPath);
        if (isDocument) {
            // Public HTML can refresh independently. Keep the workspace and its
            // framework assets on one integrity-checked release to avoid mixing versions.
            if (documentPath !== 'index.html') {
                try { return await fetch(event.request, { cache: 'no-cache' }); }
                catch { /* Fall back to the published public document while offline. */ }
            }
            const document = await cache.match(documentPath);
            if (document) return document;
        }
        // Mirror GitHub Pages' trailing-slash directory redirect, even offline.
        if (!url.pathname.endsWith('/') && self.assetsManifest.assets.some(asset => asset.url === url.pathname.slice(1) + '/index.html')) {
            url.pathname += '/';
            return Response.redirect(url.href, 301);
        }
        try { return await fetch(event.request); }
        catch (error) {
            const missing = await cache.match('404.html');
            return new Response(missing ? await missing.text() : 'Page not found', {
                status: 404, statusText: 'Not Found',
                headers: { 'Content-Type': missing ? 'text/html; charset=utf-8' : 'text/plain; charset=utf-8' }
            });
        }
    }
    return await cache.match(event.request) || fetch(event.request);
}
/* Manifest version: HUY+CxfS */
