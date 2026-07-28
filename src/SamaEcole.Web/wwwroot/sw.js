/**
 * Service Worker — Sama Ecole (PWA & Résilience des Assets Statiques).
 *
 * RÈGLES NON NÉGOCIABLES (AGENTS.md & Volume 0 §0.8) :
 * 1. Moteur de BDD = PostgreSQL exclusivement.
 * 2. Aucune base de données locale (IndexedDB / SQLite) et aucun mode hors-ligne transactionnel.
 * 3. Toute requête API (/api/v1/*) et toute mutation (POST, PUT, PATCH, DELETE) vont STRICTEMENT au serveur.
 *
 * Ce Service Worker a un rôle ciblé :
 * - Mettre en cache instantanément les assets statiques (CSS Tailwind, JS, icônes, polices) via Cache-First.
 * - Améliorer la rapidité d'affichage et le chargement sur les réseaux sénégalais instables.
 * - Fournir une résilience de navigation (Network-First avec revalidation pour les pages HTML), mais SANS JAMAIS intercepter la logique métier ni les transactions RLS.
 */

const CACHE_NAME = 'samaecole-static-v1.0.0';

const STATIC_ASSETS = [
    '/',
    '/css/site.css',
    '/js/vendor/alpine.min.js',
    '/js/auth.js',
    '/js/api.js',
    '/js/ui-components.js',
    '/js/require-session.js',
    '/js/form-draft.js',
    '/js/network-guard.js',
    '/manifest.json'
];

self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(STATIC_ASSETS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) => {
            return Promise.all(
                keys.map((key) => {
                    if (key !== CACHE_NAME) {
                        return caches.delete(key);
                    }
                })
            );
        }).then(() => self.clients.claim())
    );
});

self.addEventListener('fetch', (event) => {
    const url = new URL(event.request.url);
    const isApi = url.pathname.startsWith('/api/');
    const isMutation = event.request.method !== 'GET';

    // 1. NE JAMAIS intercepter ni cacher les requêtes API (/api/*) ou les écritures (POST, PUT, DELETE, PATCH).
    // La source de vérité est 100% serveur (PostgreSQL RLS + verrous optimistes xmin).
    if (isApi || isMutation) {
        return; // Laisser le navigateur effectuer la requête réseau directement.
    }

    // 2. Cache-First pour les assets statiques (CSS, JS, images, polices)
    if (url.pathname.startsWith('/css/') || url.pathname.startsWith('/js/') || url.pathname.startsWith('/img/') || url.pathname.endsWith('.min.js')) {
        event.respondWith(
            caches.match(event.request).then((cachedResponse) => {
                const fetchPromise = fetch(event.request).then((networkResponse) => {
                    if (networkResponse && networkResponse.status === 200) {
                        const responseToCache = networkResponse.clone();
                        caches.open(CACHE_NAME).then((cache) => cache.put(event.request, responseToCache));
                    }
                    return networkResponse;
                }).catch(() => { /* Hors ligne, ignorer la mise à jour en arrière-plan */ });

                return cachedResponse || fetchPromise;
            })
        );
        return;
    }

    // 3. Network-First pour la navigation HTML afin d'obtenir toujours la dernière vue à jour du serveur
    if (event.request.mode === 'navigate' || (event.request.headers.get('accept') && event.request.headers.get('accept').includes('text/html'))) {
        event.respondWith(
            fetch(event.request).then((networkResponse) => {
                if (networkResponse && networkResponse.status === 200) {
                    const responseToCache = networkResponse.clone();
                    caches.open(CACHE_NAME).then((cache) => cache.put(event.request, responseToCache));
                }
                return networkResponse;
            }).catch(() => {
                return caches.match(event.request).then((cachedPage) => {
                    return cachedPage || caches.match('/');
                });
            })
        );
        return;
    }
});
