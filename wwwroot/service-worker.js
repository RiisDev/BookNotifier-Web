// This is a Blazor Server app: every page needs a live SignalR connection to the server,
// so a real "offline mode" isn't possible here — don't cache HTML, _blazor/_framework, or
// anything dynamic. This service worker exists to (a) satisfy PWA installability criteria
// (a registered SW with a fetch handler) and (b) cache the handful of genuinely static
// assets below so repeat loads/icons are instant.
const CACHE = "booknotifier-static-v1";
const STATIC_ASSETS = ["/icons/icon-192.png", "/icons/icon-512.png", "/apple-touch-icon.png", "/manifest.webmanifest"];

self.addEventListener("install", (event) => {
	event.waitUntil(caches.open(CACHE).then((cache) => cache.addAll(STATIC_ASSETS)));
	self.skipWaiting();
});

self.addEventListener("activate", (event) => {
	event.waitUntil(
		caches.keys().then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
	);
	self.clients.claim();
});

self.addEventListener("fetch", (event) => {
	const url = new URL(event.request.url);
	if (!STATIC_ASSETS.includes(url.pathname)) return; // everything else: network, untouched
	event.respondWith(caches.match(event.request).then((cached) => cached ?? fetch(event.request)));
});
