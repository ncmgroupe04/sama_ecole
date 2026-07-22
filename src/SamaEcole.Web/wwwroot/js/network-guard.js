/**
 * Guard Réseau et Enregistrement Service Worker (PWA & Résilience).
 * Conforme au Volume 0 §0.8 et aux règles AGENTS.md (aucun mode hors-ligne complet, pas de base locale).
 *
 * Détecte les coupures de connexion, affiche un bandeau d'information discret
 * lorsque le poste est déconnecté, et enregistre le Service Worker de mise en cache
 * des assets statiques (Tailwind CSS, polices, scripts JS).
 */
window.networkGuard = {
    isOnline: navigator.onLine,
    listeners: [],

    init() {
        window.addEventListener('online', () => {
            this.isOnline = true;
            this.notifyListeners(true);
        });

        window.addEventListener('offline', () => {
            this.isOnline = false;
            this.notifyListeners(false);
        });

        this.registerServiceWorker();
    },

    onChange(cb) {
        if (typeof cb === 'function') {
            this.listeners.push(cb);
        }
    },

    notifyListeners(online) {
        this.listeners.forEach((cb) => {
            try { cb(online); } catch (e) { console.error(e); }
        });
    },

    registerServiceWorker() {
        if ('serviceWorker' in navigator && (window.location.protocol === 'https:' || window.location.hostname === 'localhost' || window.location.hostname === '127.0.0.1')) {
            window.addEventListener('load', () => {
                navigator.serviceWorker.register('/sw.js', { scope: '/' })
                    .then((reg) => {
                        console.debug('SamaEcole ServiceWorker enregistré avec succès. Scope :', reg.scope);
                    })
                    .catch((err) => {
                        console.warn('Échec de l\'enregistrement du ServiceWorker :', err);
                    });
            });
        }
    }
};

// Initialisation immédiate du Guard Réseau
window.networkGuard.init();

/**
 * Composant Alpine pour le bandeau d'alerte de résilience réseau global.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('networkGuardBanner', () => ({
        online: window.networkGuard.isOnline,
        showReconnectedToast: false,

        init() {
            window.networkGuard.onChange((status) => {
                const wasOffline = !this.online;
                this.online = status;
                if (status && wasOffline) {
                    this.showReconnectedToast = true;
                    setTimeout(() => { this.showReconnectedToast = false; }, 5000);
                }
            });
        }
    }));
});
