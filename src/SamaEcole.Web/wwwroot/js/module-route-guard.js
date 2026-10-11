/*
 * Garde de page par module (spec §8.3, lot G) — module-route-guard.js.
 *
 * Une page déclare le module dont elle dépend par [PageModule(SchoolModule.X)] sur son action (PagesController) ; _Layout
 * émet <meta name="required-module" content="X">. Ici on lit cette balise, on attend les réglages de l'école
 * (Alpine.store('schoolConfig')) et on décide :
 *   - module activé                           → on affiche ;
 *   - module désactivé (réglages bien lus)    → on redirige vers l'atterrissage du rôle, avec un message ;
 *   - ÉCHEC de lecture des réglages           → on ne redirige JAMAIS : bandeau « Réessayer ». Sinon une coupure réseau
 *                                               (fréquente, cf. network-guard.js) éjecterait à tort un Directeur de Daara de son
 *                                               module principal. C'est la même idée que l'état `undefined` de
 *                                               profileEtablissement, qui ne force jamais de redirection.
 *
 * CE N'EST PAS UNE BARRIÈRE D'ACCÈS. Les gabarits sont servis sans authentification : le serveur ne connaît ni l'école ni le profil
 * au moment du GET. Un utilisateur qui force l'URL récupère un gabarit vide de données ; chaque appel d'API répond 403
 * MODULE_DISABLED et la RLS isole les écoles. La redirection est de l'ergonomie.
 *
 * Le contenu (<main>) est masqué dès le chargement du script (attribut data-module-guard="pending" + règle CSS injectée) jusqu'à la
 * décision, pour éviter le flash d'un écran vide ; il reste masqué pendant une redirection.
 *
 * Aucune boucle de redirection : l'atterrissage d'un profil peut lui-même être une page gardée (Directeur Daara → /suivi-coranique
 * alors que le module Internat vient d'être désactivé). Une seconde redirection dans les secondes qui suivent la première, ou vers
 * la page courante, utilise le repli neutre (auth.neutralLandingForRole), qui n'est jamais une page gardée.
 *
 * Chargé après auth.js, api.js et ui-components.js (toast), avant Alpine : l'écouteur alpine:init est enregistré à temps, après celui
 * de auth.js qui déclare le store schoolConfig.
 */
window.moduleRouteGuard = (() => {
    const ATTRIBUTE = 'data-module-guard';
    const NOTICE_KEY = 'unikol.moduleGuard.notice';
    const LAST_REDIRECT_KEY = 'unikol.moduleGuard.lastRedirect';
    const LOOP_WINDOW_MS = 15000;
    const FAILSAFE_MS = 8000;
    const NOT_ENABLED_MESSAGE = "Ce module n'est pas activé pour votre établissement.";

    /** Interrupteur du store pour chaque module connu. Un module inconnu ne bloque jamais. */
    const ENABLED_FLAG = {
        Internat: 'internatEnabled',
        Coran: 'coranEnabled',
        Pedagogy: 'pedagogyEnabled',
        Finance: 'financeEnabled'
    };

    /** Décision pure : 'show' | 'redirect' | 'unverified'. */
    function decide({ requiredModule, store }) {
        if (!requiredModule) return 'show';

        const flag = ENABLED_FLAG[requiredModule];
        if (!flag) return 'show';
        if (store[flag] === true) return 'show';
        return store.loadFailed ? 'unverified' : 'redirect';
    }

    function safeStorage() {
        try {
            return window.sessionStorage || null;
        } catch {
            return null;
        }
    }

    function readStorage(storage, key) {
        try {
            return storage ? storage.getItem(key) : null;
        } catch {
            return null;
        }
    }

    function writeStorage(storage, key, value) {
        try {
            if (storage) storage.setItem(key, value);
        } catch {
            // Stockage indisponible (navigation privée…) : la garde fonctionne sans, seul le message différé se perd.
        }
    }

    function reveal(doc) {
        doc.documentElement.setAttribute(ATTRIBUTE, 'done');
    }

    function showUnverifiedBanner(doc, onRetry) {
        const main = doc.querySelector('main');
        if (!main) return null;

        const banner = doc.createElement('div');
        banner.className = 'form-warning-banner mx-4 mt-4 flex items-center justify-between gap-3';
        banner.setAttribute && banner.setAttribute('role', 'alert');

        const text = doc.createElement('span');
        text.textContent = 'Impossible de vérifier les modules activés pour votre établissement.';
        banner.appendChild(text);

        const retry = doc.createElement('button');
        retry.className = 'btn btn-secondary shrink-0';
        retry.textContent = 'Réessayer';
        retry.addEventListener('click', () => onRetry(banner));
        banner.appendChild(retry);

        main.prepend(banner);
        return banner;
    }

    /**
     * Applique la décision. Les dépendances sont injectables pour les tests ; en production elles viennent de la page.
     * Renvoie la décision finale ('show' | 'redirect' | 'unverified').
     */
    async function apply(deps = {}) {
        const doc = deps.doc || document;
        const win = deps.win || window;
        const meta = doc.querySelector('meta[name="required-module"]');
        const requiredModule = meta ? meta.getAttribute('content') : null;

        if (!requiredModule) {
            reveal(doc);
            return 'show';
        }

        const auth = deps.auth || win.auth;
        // Super Admin (pas d'école) et visiteur non connecté (require-session.js s'en charge) : la garde ne décide pas pour eux.
        if (!auth || !auth.isAuthenticated() || auth.role === 'SuperAdmin') {
            reveal(doc);
            return 'show';
        }

        const store = deps.store || Alpine.store('schoolConfig');
        const storage = deps.storage || safeStorage();
        const now = (deps.now || Date.now)();

        await store.init();
        const decision = decide({ requiredModule, store });

        if (decision === 'redirect') {
            const last = Number(readStorage(storage, LAST_REDIRECT_KEY)) || 0;
            const justBounced = last > 0 && now - last < LOOP_WINDOW_MS;

            let target = auth.defaultLandingForRole();
            if (justBounced || target === win.location.pathname) target = auth.neutralLandingForRole();

            writeStorage(storage, LAST_REDIRECT_KEY, String(now));
            // Le message s'affiche sur la page d'arrivée : un toast posé juste avant location.replace disparaîtrait avec elle.
            writeStorage(storage, NOTICE_KEY, NOT_ENABLED_MESSAGE);
            win.location.replace(target);
            return 'redirect';
        }

        reveal(doc);

        if (decision === 'unverified') {
            showUnverifiedBanner(doc, async (banner) => {
                await store.retry();
                if (decide({ requiredModule, store }) === 'unverified') return;

                banner.remove();
                await apply(deps);
            });
        }

        return decision;
    }

    /** Affiche une seule fois le message laissé par la redirection précédente. */
    function showPendingNotice(deps = {}) {
        const storage = deps.storage || safeStorage();
        const toast = deps.toast || window.toast;
        const message = readStorage(storage, NOTICE_KEY);
        if (!message) return;

        try {
            if (storage) storage.removeItem(NOTICE_KEY);
        } catch {
            // voir writeStorage
        }

        if (toast && toast.error) toast.error(message);
    }

    /** Masque <main> dès le chargement quand la page déclare un module : évite le flash avant la décision. */
    function hideUntilDecided() {
        try {
            if (!document.querySelector || !document.querySelector('meta[name="required-module"]')) return;

            const style = document.createElement('style');
            style.textContent = `html[${ATTRIBUTE}="pending"] main { visibility: hidden; }`;
            document.head.appendChild(style);
            document.documentElement.setAttribute(ATTRIBUTE, 'pending');

            // Filet de sécurité : si la décision n'arrive jamais (Alpine qui ne démarre pas, erreur de script), la page ne reste pas blanche.
            setTimeout(() => {
                if (document.documentElement.getAttribute(ATTRIBUTE) === 'pending') reveal(document);
            }, FAILSAFE_MS);
        } catch {
            // Masquer est un confort : sans cela, la page s'affiche simplement avant la décision.
        }
    }

    hideUntilDecided();
    document.addEventListener('alpine:init', () => {
        showPendingNotice();
        // Une erreur inattendue ne doit jamais laisser le contenu masqué : l'API reste le juge.
        apply().catch(() => reveal(document));
    });

    return { decide, apply, showPendingNotice };
})();
