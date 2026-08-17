const CACHE_NAME = 'nebula-reader-v2';
const APP_SHELL = [
  '/', '/index.html', '/manifest.webmanifest', '/icons/icon.svg', '/icons/icon-192.png', '/icons/icon-512.png',
  '/vendor/jszip.min.js', '/vendor/worker-bundle.js', '/vendor/libarchive.wasm',
  '/src/styles.css', '/src/app.js', '/src/db.js', '/src/archive.js',
  '/assets/style.css', '/assets/app.js', '/assets/pdf-engine.js', '/assets/rar-engine.js', '/assets/native-orientation.js',
];

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE_NAME);
    await Promise.allSettled(APP_SHELL.map((url) => cache.add(url)));
    try {
      const response = await fetch('/vendor/pdfjs/asset-manifest.json', { cache: 'no-store' });
      if (response.ok) {
        const manifest = await response.clone().json();
        await cache.put('/vendor/pdfjs/asset-manifest.json', response);
        for (let index = 0; index < manifest.assets.length; index += 20) {
          await Promise.allSettled(manifest.assets.slice(index, index + 20).map((url) => cache.add(url)));
        }
      }
    } catch (error) {
      console.warn('No se pudieron precargar todos los recursos PDF.', error);
    }
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener('fetch', (event) => {
  if (event.request.method !== 'GET' || new URL(event.request.url).origin !== self.location.origin) return;
  event.respondWith((async () => {
    const cached = await caches.match(event.request);
    if (cached) return cached;
    try {
      const response = await fetch(event.request);
      if (response.ok) {
        const cache = await caches.open(CACHE_NAME);
        cache.put(event.request, response.clone());
      }
      return response;
    } catch (error) {
      if (event.request.mode === 'navigate') return caches.match('/index.html');
      throw error;
    }
  })());
});
