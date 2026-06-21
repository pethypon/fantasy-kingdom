// 最小のオフラインキャッシュ（静的アセットのみ）。
// API結果はキャッシュせず常に最新を取りに行く。
const CACHE = "trade-assist-v1";
const ASSETS = [
  "index.html",
  "style.css",
  "app.js",
  "manifest.json",
  "icon.svg",
];

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(CACHE).then((c) => c.addAll(ASSETS)));
  self.skipWaiting();
});

self.addEventListener("activate", (e) => {
  e.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)))
    )
  );
  self.clients.claim();
});

self.addEventListener("fetch", (e) => {
  const url = new URL(e.request.url);
  if (url.pathname.startsWith("/api/")) return; // APIはネットワーク優先
  e.respondWith(
    caches.match(e.request).then((cached) => cached || fetch(e.request))
  );
});
