/*
 * Garde-fou « Mode test » — empêche qu'un Directeur saisisse de VRAIES données (élèves, paiements,
 * notes) alors que son établissement est encore en bac à sable.
 *
 * Deux briques, toutes deux alimentées par la même lecture de GET /schools/current/mode :
 *   - window.schoolMode.get() : lecture mise en cache (une seule requête par page) partagée avec la
 *     pastille de régime de la barre supérieure (auth.js › sandboxModeBadge) ;
 *   - liveModeGuard() : la modale d'avertissement (_LiveModeWarningModal.cshtml), qui s'ouvre à
 *     l'arrivée sur un module de gestion opérationnelle.
 *
 * N'ACTIVE rien et ne bloque rien : la bascule test → réel reste un acte confirmé du Directeur
 * (POST /schools/current/go-live), et chaque route API garde son autorisation. Ici on GUIDE.
 *
 * Chargé après auth.js et api.js, avant Alpine (script classique : l'écouteur alpine:init est
 * enregistré à temps). Dépend de window.auth, window.api et — pour le critère « paramétrage de base
 * terminé » — de setup-assistant.js, qui publie `setup-progress`.
 */
window.schoolMode = (() => {
    let pending = null;
    return {
        /** Mode de l'établissement courant (`isLive`, …). Un échec n'est pas mémorisé : on retentera. */
        get() {
            if (!pending) {
                pending = window.api.get('/schools/current/mode').catch((err) => {
                    pending = null;
                    throw err;
                });
            }
            return pending;
        }
    };
})();

document.addEventListener('alpine:init', () => {
    Alpine.data('liveModeGuard', () => ({
        // Modules de gestion opérationnelle : inscription d'élèves, paiements (Caisse), saisie des notes.
        GUARDED_PATHS: ['/inscriptions', '/caisse', '/notes'],

        // Rôles qui pilotent le paramétrage (les seuls dont l'assistant de démarrage évalue l'état).
        // Les autres rôles (Finance, Enseignant…) n'ont pas accès aux routes de paramétrage : on ne
        // peut pas juger de l'état de l'école, donc on les avertit sans attendre — côté prudent.
        SETUP_ROLES: ['Directeur', 'Secretariat'],

        // Une fois par session navigateur (et non une fois pour toutes) : la modale revient à chaque
        // nouvelle connexion tant que l'établissement est en mode test — le risque persiste.
        SEEN_KEY: 'unikol.liveModeWarning.shown',

        // Chemin exact à afficher ET destination du bouton — un seul endroit pour ne pas les désaccorder.
        SETTINGS_URL: '/parametres?tab=securite#mode-reel',

        open: false,
        isDirecteur: window.auth.role === 'Directeur',

        async init() {
            if (!window.auth || !window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') return;
            if (!this.isGuardedPage() || this.alreadyShown()) return;

            let mode;
            try {
                mode = await window.schoolMode.get();
            } catch {
                return; // Confort d'affichage : sans réponse fiable, on ne crie pas « mode test ».
            }
            if (!mode || mode.isLive) return;

            if (this.SETUP_ROLES.includes(window.auth.role) && !(await this.baseSetupComplete())) return;

            this.present();
        },

        isGuardedPage() {
            const path = String((window.location && window.location.pathname) || '')
                .toLowerCase()
                .replace(/\/+$/, '');
            return this.GUARDED_PATHS.some((p) => path === p || path.startsWith(p + '/'));
        },

        alreadyShown() {
            try {
                return window.sessionStorage.getItem(this.SEEN_KEY) === '1';
            } catch {
                return false;
            }
        },

        /**
         * « Paramétrage de base terminé » = état publié par l'assistant de démarrage (setup-assistant.js,
         * `setup-progress`) : lu tout de suite s'il est déjà connu, sinon attendu.
         */
        baseSetupComplete() {
            const progress = window.setupProgress;
            if (progress && progress.baseComplete !== null && progress.baseComplete !== undefined) {
                return Promise.resolve(!!progress.baseComplete);
            }
            return new Promise((resolve) => {
                window.addEventListener('setup-progress', (event) => {
                    resolve(!!(event.detail && event.detail.baseComplete));
                }, { once: true });
            });
        },

        present() {
            try {
                window.sessionStorage.setItem(this.SEEN_KEY, '1');
            } catch {
                // sessionStorage indisponible : la modale pourra se rouvrir à la page suivante, c'est acceptable.
            }

            // Une seule modale principale à la fois — même précaution que setup-assistant.js › toggle().
            // Ici l'assistant de démarrage peut s'être ouvert seul au premier accès : l'avertissement
            // de sécurité passe avant. closeAllModals() diffuse aussi `close-modals` à NOTRE modale ;
            // on ouvre donc au tick suivant, une fois le broadcast passé.
            setTimeout(() => {
                if (window.closeAllModals) window.closeAllModals();
                setTimeout(() => { this.open = true; }, 0);
            }, 0);
        },

        close() {
            this.open = false;
        },

        goToSettings() {
            window.location.assign(this.SETTINGS_URL);
        }
    }));
});
