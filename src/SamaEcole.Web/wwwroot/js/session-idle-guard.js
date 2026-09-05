/**
 * Déconnexion automatique par inactivité (Paramètres > Sécurité, config.autoLogoutMinutes).
 *
 * Le réglage existait déjà (formulaire Paramètres, SchoolSettingsDto.AutoLogoutMinutes) mais rien ne
 * l'appliquait jamais côté client : un Directeur pouvait le fixer à 10 minutes, une session restait
 * ouverte indéfiniment tant que l'onglet l'était. Ce module est la SEULE chose qui l'applique
 * réellement — le serveur ne révoque pas le JWT de son côté à l'expiration du délai, il continue
 * simplement d'accepter le jeton jusqu'à SA propre expiration (voir auth.js/refresh).
 *
 * Chargé une fois par _Layout.cshtml, après auth.js et api.js dont il dépend. Script classique
 * (pas de defer) : s'auto-initialise dès son exécution, pas besoin d'attendre Alpine.
 */
(() => {
    const ACTIVITY_EVENTS = ['mousedown', 'mousemove', 'keydown', 'wheel', 'touchstart', 'scroll'];
    // Granularité de vérification : la minute est la plus petite unité du réglage, inutile de
    // vérifier plus souvent qu'un intervalle très inférieur à la plus petite valeur autorisée (1 min).
    const CHECK_INTERVAL_MS = 15000;

    window.sessionIdleGuard = {
        timeoutMs: null,
        lastActivityAt: Date.now(),
        checkTimer: null,
        loggingOut: false,

        async init() {
            // Le Super Admin n'a aucun tenant (console plateforme) : /schools/current/settings n'a
            // pas de sens pour lui, et rien ne l'exige — ticket AUTO-LOGOUT porte sur les comptes
            // d'établissement.
            if (!window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') return;

            try {
                const settings = await window.api.get('/schools/current/settings');
                const minutes = Number(settings && settings.autoLogoutMinutes);
                if (!Number.isFinite(minutes) || minutes <= 0) return;
                this.timeoutMs = minutes * 60000;
            } catch (e) {
                // Réseau indisponible, rôle sans école, etc. : pas de valeur exploitable, on
                // n'invente pas de délai par défaut plutôt que de déconnecter sur une hypothèse.
                return;
            }

            const record = () => this.recordActivity();
            ACTIVITY_EVENTS.forEach((evt) => window.addEventListener(evt, record, { passive: true }));
            // Un onglet reste en arrière-plan des heures sans qu'aucun évènement d'activité ne s'y
            // déclenche : à la reprise de visibilité, on ne compte pas ce temps masqué comme de
            // l'inactivité active, mais checkIdle() ci-dessous s'en chargera immédiatement si le
            // délai est déjà dépassé au retour.
            document.addEventListener('visibilitychange', () => {
                if (!document.hidden) this.checkIdle();
            });

            this.checkTimer = setInterval(() => this.checkIdle(), CHECK_INTERVAL_MS);
        },

        recordActivity() {
            this.lastActivityAt = Date.now();
        },

        checkIdle() {
            if (this.loggingOut || !this.timeoutMs || !window.auth.isAuthenticated()) return;
            if (Date.now() - this.lastActivityAt < this.timeoutMs) return;

            this.loggingOut = true;
            clearInterval(this.checkTimer);
            window.auth.logout();
        }
    };

    window.sessionIdleGuard.init();
})();
