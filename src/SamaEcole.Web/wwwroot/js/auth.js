/**
 * Session côté navigateur — ticket JGK-A04.
 *
 * Deux jetons, deux traitements très différents :
 *
 *   - L'ACCESS TOKEN (15 min) vit dans localStorage. Il faut qu'il survive aux navigations, car
 *     l'application est en Razor multi-pages : chaque clic recharge la page et effacerait un jeton
 *     gardé en mémoire, ce qui forcerait un renouvellement à chaque écran.
 *
 *   - Le REFRESH TOKEN (14 jours) n'est JAMAIS visible d'ici. Le serveur le pose en cookie HttpOnly
 *     (docs/Volume_4_API_Design.md §1.1, SamaEcole.Web/Auth/RefreshTokenCookie.cs) : le navigateur
 *     le joint tout seul aux appels /api/v1/auth/*, et aucun script — donc aucune XSS — ne peut le
 *     lire. C'est pour cela qu'on ne le trouvera nulle part dans ce fichier.
 */
(() => {
    'use strict';

    const API_BASE = '/api/v1';
    const LOGIN_PATH = '/login';
    const DEFAULT_LANDING = '/eleves';

    // Le Super Admin n'a AUCUN établissement : /eleves (comme tout écran tenant) est vide pour lui,
    // la RLS lui fermant toutes les tables d'école. Son point d'entrée est la console plateforme
    // (tableau de bord global) — la revue des demandes d'inscription (JGK-I03) reste accessible
    // depuis son propre menu, sous /admin/inscriptions.
    const SUPER_ADMIN_LANDING = '/admin';

    // JGK-F04 : Directeur et Finance ont un tableau de bord dédié (voir PagesController.Dashboard) et
    // y atterrissent directement. Secrétariat et Enseignant, qui n'y ont pas accès (_Layout.cshtml,
    // sidebarNav), gardent l'atterrissage tenant par défaut sur /eleves.
    const DASHBOARD_LANDING = '/tableau-de-bord';
    const DASHBOARD_ROLES = ['Directeur', 'Finance'];

    const STORAGE_KEYS = {
        accessToken: 'sama_ecole.access_token',
        expiresAt: 'sama_ecole.access_token_expires_at',
        email: 'sama_ecole.email'
    };

    /**
     * On considère le jeton périmé une minute avant son échéance réelle : une requête partie juste
     * avant l'expiration arriverait sinon expirée au serveur, et le ClockSkew de 30 s côté API
     * (Program.cs) ne couvre pas un poste dont l'horloge dérive.
     */
    const EXPIRY_SKEW_MS = 60_000;

    /** Portée du verrou de renouvellement — voir refresh(). */
    const REFRESH_LOCK = 'sama-ecole.auth-refresh';

    /** Repli pour les navigateurs sans Web Locks : au moins un seul renouvellement en vol par onglet. */
    let inFlightRefresh = null;

    /** Traduit une réponse d'erreur en Error exploitable, que le corps soit du JSON normalisé ou vide. */
    async function toError(response) {
        // Un 401 émis par le middleware JWT (jeton absent/expiré) n'a pas de corps : il ne passe pas
        // par ExceptionHandlingMiddleware, qui ne voit que les exceptions applicatives.
        const payload = await response.json().catch(() => null);

        if (payload && payload.message) {
            const error = new Error(payload.message);
            error.code = payload.code;
            error.details = payload.details;
            error.status = response.status;
            return error;
        }

        const fallback = new Error(`Erreur HTTP ${response.status}`);
        fallback.status = response.status;
        return fallback;
    }

    /** Lit les claims de l'access token sans vérifier la signature — l'affichage seulement, jamais une décision de sécurité. */
    function readClaims(token) {
        if (!token) return null;

        try {
            const payload = token.split('.')[1];
            const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
            return JSON.parse(json);
        } catch {
            return null;
        }
    }

    /** Échange le cookie de refresh contre un nouveau couple de jetons. Le cookie part tout seul. */
    async function fetchNewTokens() {
        const response = await fetch(`${API_BASE}/auth/refresh`, {
            method: 'POST',
            credentials: 'same-origin'
        });

        if (!response.ok) {
            auth.clearSession();
            throw await toError(response);
        }

        const tokens = await response.json();
        auth.saveSession(tokens);
        return tokens.accessToken;
    }

    const auth = {
        get accessToken() {
            return localStorage.getItem(STORAGE_KEYS.accessToken);
        },

        get email() {
            return localStorage.getItem(STORAGE_KEYS.email) || '';
        },

        get name() {
            const claims = readClaims(this.accessToken);
            return claims ? claims.name || '' : '';
        },

        get role() {
            const claims = readClaims(this.accessToken);
            return claims ? claims.role || '' : '';
        },

        /** Ticket JGK-I05 : l'écran de paiement d'abonnement en a besoin pour construire l'URL /subscriptions/{schoolId}/payments. */
        get schoolId() {
            const claims = readClaims(this.accessToken);
            return claims ? claims.schoolId || '' : '';
        },

        /**
         * Console Super Admin (bouton « Infiltrer ») : présent uniquement sur un jeton d'impersonation
         * (JwtTokenGenerator.GenerateImpersonation) — l'id du Super Admin réel derrière la session.
         */
        get impersonatedBy() {
            const claims = readClaims(this.accessToken);
            return claims ? claims.impersonatedBy || '' : '';
        },

        isImpersonating() {
            return Boolean(this.impersonatedBy);
        },

        /** Un jeton présent mais périmé n'est pas une session : il reste renouvelable tant que le cookie vit. */
        isAuthenticated() {
            return Boolean(this.accessToken);
        },

        isAccessTokenStale() {
            const expiresAt = Number(localStorage.getItem(STORAGE_KEYS.expiresAt));
            return !expiresAt || Date.now() >= expiresAt - EXPIRY_SKEW_MS;
        },

        /** `email` n'est fourni qu'à la connexion : un renouvellement ne doit pas l'effacer. */
        saveSession(tokens, email) {
            localStorage.setItem(STORAGE_KEYS.accessToken, tokens.accessToken);
            localStorage.setItem(
                STORAGE_KEYS.expiresAt,
                String(Date.now() + tokens.expiresIn * 1000));

            if (email) {
                localStorage.setItem(STORAGE_KEYS.email, email);
            }
        },

        clearSession() {
            Object.values(STORAGE_KEYS).forEach((key) => localStorage.removeItem(key));
        },

        async login(email, password) {
            const response = await fetch(`${API_BASE}/auth/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin', // indispensable : c'est ainsi que le cookie de refresh est accepté
                body: JSON.stringify({ email, password })
            });

            if (!response.ok) {
                throw await toError(response);
            }

            auth.saveSession(await response.json(), email);
        },

        /**
         * Console Super Admin (bouton « Infiltrer ») : bascule la session sur le jeton d'impersonation
         * renvoyé par POST /admin/platform/schools/{schoolId}/impersonate. Ce n'est PAS une nouvelle
         * connexion — le cookie de refresh du Super Admin reste intact et inutilisé tant que dure
         * l'impersonation, c'est lui qui permet de revenir (voir exitImpersonation).
         */
        enterImpersonation(tokens) {
            auth.saveSession(tokens);
        },

        /**
         * Sort d'une session d'impersonation AVANT son expiration naturelle (~15 min) : échange le
         * cookie de refresh — celui du Super Admin, jamais touché par enterImpersonation — contre ses
         * propres jetons. Contrairement à refresh(), on ignore délibérément isAccessTokenStale() : le
         * jeton d'impersonation en cours n'est pas expiré, c'est une sortie volontaire.
         */
        async exitImpersonation() {
            await fetchNewTokens();
            window.location.assign(SUPER_ADMIN_LANDING);
        },

        /**
         * Renouvelle l'access token, en garantissant qu'UN SEUL renouvellement tourne à la fois pour
         * toute l'origine — tous onglets confondus.
         *
         * Ce n'est pas du confort. Le serveur fait tourner le refresh token et, si un jeton déjà
         * consommé lui est représenté, il y voit un rejeu (vol probable) et révoque TOUTE la famille
         * de jetons de l'utilisateur — RefreshTokenCommandHandler. Or deux onglets qui renouvellent
         * en même temps présentent forcément le même cookie : sans verrou, le second passerait pour
         * un attaquant et l'utilisateur serait déconnecté sèchement, en plein travail.
         *
         * Web Locks est partagé entre les onglets de la même origine. Celui qui obtient le verrou
         * renouvelle ; les autres attendent, puis constatent que le jeton en stockage est déjà frais
         * et repartent avec, sans retoucher au cookie.
         */
        async refresh() {
            const run = async () => {
                if (!auth.isAccessTokenStale()) {
                    return auth.accessToken; // un autre onglet a déjà renouvelé pendant l'attente
                }

                return await fetchNewTokens();
            };

            if (navigator.locks) {
                return navigator.locks.request(REFRESH_LOCK, run);
            }

            inFlightRefresh = inFlightRefresh || run().finally(() => { inFlightRefresh = null; });
            return inFlightRefresh;
        },

        async logout() {
            const token = auth.accessToken;

            // Au mieux : même si l'appel échoue (réseau coupé, jeton déjà expiré), on quitte la
            // session localement. Sans jeton, on ne tente rien — /auth/logout exige une
            // authentification, et un en-tête « Bearer null » ne ferait qu'un 401 de plus.
            if (token) {
                try {
                    await fetch(`${API_BASE}/auth/logout`, {
                        method: 'POST',
                        credentials: 'same-origin',
                        headers: { Authorization: `Bearer ${token}` }
                    });
                } catch {
                    /* ignoré volontairement */
                }
            }

            auth.clearSession();
            window.location.assign(LOGIN_PATH);
        },

        redirectToLogin() {
            const returnUrl = window.location.pathname + window.location.search;
            window.location.assign(`${LOGIN_PATH}?returnUrl=${encodeURIComponent(returnUrl)}`);
        },

        /**
         * Garde-fou d'affichage, appelé en <head> pour éviter que le contenu n'apparaisse une
         * fraction de seconde avant la redirection. Ne protège AUCUNE donnée : les données ne
         * viennent que de l'API, qui exige le JWT et applique la RLS.
         */
        requireSession() {
            if (!auth.isAuthenticated()) {
                auth.redirectToLogin();
            }
        },

        /** Atterrissage par défaut selon le rôle : le Super Admin n'a pas de tenant, on l'oriente vers son espace. */
        defaultLandingForRole() {
            if (this.role === 'SuperAdmin') return SUPER_ADMIN_LANDING;
            if (DASHBOARD_ROLES.includes(this.role)) return DASHBOARD_LANDING;
            return DEFAULT_LANDING;
        },

        /** Inverse : inutile de réafficher l'écran de connexion à quelqu'un qui a déjà une session. */
        redirectIfAuthenticated() {
            if (auth.isAuthenticated()) {
                window.location.replace(auth.defaultLandingForRole());
            }
        },

        /**
         * Cible de redirection après connexion. On n'accepte qu'un chemin interne : une URL absolue
         * dans ?returnUrl= permettrait d'envoyer l'utilisateur fraîchement connecté sur un site tiers
         * (open redirect), et « //evil.tld » est une URL absolue déguisée.
         */
        landingUrl() {
            const target = new URLSearchParams(window.location.search).get('returnUrl');
            const isInternalPath = target && target.startsWith('/') && !target.startsWith('//');

            return isInternalPath ? target : auth.defaultLandingForRole();
        }
    };

    window.auth = auth;
})();

document.addEventListener('alpine:init', () => {
    Alpine.data('loginForm', () => ({
        email: '',
        password: '',
        showPassword: false,
        isSubmitting: false,
        error: null,

        async submit() {
            this.error = null;
            this.isSubmitting = true;

            try {
                await window.auth.login(this.email, this.password);
                window.location.assign(window.auth.landingUrl());
            } catch (err) {
                // Le serveur renvoie le MÊME message pour un e-mail inconnu, un mot de passe faux et
                // un compte verrouillé (LoginCommandHandler) : ne rien ajouter ici qui permettrait de
                // distinguer les cas, sous peine de rouvrir l'énumération de comptes qu'il évite.
                this.error = err.message || 'Connexion impossible. Vérifiez votre réseau et réessayez.';
                this.password = '';
            } finally {
                this.isSubmitting = false;
            }
        }
    }));

    /** Bandeau utilisateur de la barre supérieure : e-mail saisi à la connexion + rôle lu dans le JWT. */
    Alpine.data('sessionMenu', () => ({
        email: window.auth.email,
        name: window.auth.name,
        role: window.auth.role,
        logout: () => window.auth.logout()
    }));

    /**
     * Bandeau d'impersonation (_Layout.cshtml) : visible uniquement quand la session courante vient du
     * bouton « Infiltrer » de la console Super Admin (claim impersonatedBy, voir auth.js isImpersonating).
     * Évalué une fois au chargement de la page — une impersonation ne démarre/finit jamais SANS
     * navigation complète (enterImpersonation/exitImpersonation redirigent toujours), inutile de la
     * suivre en réactif.
     */
    Alpine.data('impersonationBanner', () => ({
        isImpersonating: window.auth.isImpersonating(),
        isExiting: false,

        async exit() {
            this.isExiting = true;
            try {
                await window.auth.exitImpersonation();
            } catch {
                window.auth.redirectToLogin();
            }
        }
    }));

    /**
     * Visibilité du menu latéral par rôle (JGK-D05, Volume 5 §3.2) : masquer un onglet est une
     * commodité d'ergonomie, jamais une mesure de sécurité — chaque route reste protégée côté API
     * indépendamment de l'affichage (voir le commentaire de PagesController). Les listes de rôles
     * reprennent la colonne « Voir » de la matrice détaillée (Volume 7 §15), module par module.
     *
     * Matières, Présences et Rapports sont absents de la matrice détaillée : leurs listes de rôles
     * viennent d'une confirmation produit directe plutôt que du Volume 7 §15. Administration est
     * réservée au Super Admin sur mention explicite du Volume 5 §3.2, bien qu'aucune route ne soit
     * encore livrée (Module B du backlog).
     */
    Alpine.data('sidebarNav', () => ({
        role: window.auth.role,

        /**
         * Vrai pour les établissements publics sénégalais : Caisse, Finance et Dashboard financier
         * sont masqués dans la navigation (l'API reste accessible, c'est une contrainte d'affichage).
         * Chargé en `init` via GET /schools/current/settings — non bloquant : en cas d'erreur
         * réseau, la valeur reste false (mode Privé par défaut, accès complet Finance conservé).
         */
        isPublicSchool: false,

        async init() {
            // Super Admin plateforme : pas d'école, pas de settings. On laisse false.
            if (!window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') return;
            try {
                const s = await window.api.get('/schools/current/settings');
                this.isPublicSchool = (s && s.typeEtablissement === 'Public');
            } catch {
                // Non bloquant : en cas d'erreur réseau, la sidebar reste complète (sûr par défaut).
                this.isPublicSchool = false;
            }
        },

        canView(roles) { return roles.includes(this.role); }
    }));

    /**
     * Indicateur réseau de la barre d'état (Volume 5 §9).
     *
     * Il s'aligne sur window.networkGuard plutôt que d'écouter `online`/`offline` pour son compte :
     * ces événements ne reflètent que l'état de l'interface réseau, tandis que le guard confirme par
     * une sonde que le SERVEUR répond. Deux voyants de la même page qui mesurent deux choses
     * différentes finissent par se contredire à l'écran — pastille verte en bas, badge orange en
     * haut — et c'est le voyant optimiste que l'utilisateur croit.
     *
     * networkGuard est chargé après auth.js, mais `alpine:init` ne se déclenche qu'une fois tous les
     * scripts classiques exécutés : il est donc toujours présent ici. Le repli sur navigator.onLine
     * ne sert qu'aux gabarits qui n'incluraient pas network-guard.js.
     */
    Alpine.data('networkStatus', () => ({
        online: window.networkGuard ? window.networkGuard.isOnline : navigator.onLine,

        init() {
            if (window.networkGuard) {
                // 'checking' est transitoire : y réagir ferait virer la pastille au rouge à chaque
                // vérification de routine, alors que rien n'est encore établi.
                window.networkGuard.onChange((online, state) => {
                    if (state !== 'checking') this.online = online;
                });
                return;
            }

            window.addEventListener('online', () => { this.online = true; });
            window.addEventListener('offline', () => { this.online = false; });
        }
    }));

    /**
     * Indicateur d'ANNÉE SCOLAIRE ACTIVE de la barre supérieure (JGK-C01). Il rend visible sur CHAQUE
     * écran l'exercice sur lequel travaille l'établissement — inscriptions, frais, notes, bulletins et
     * finances s'y rattachent, résolus serveur via SchoolYear.IsActive. Sans cet indice, changer
     * d'année ne produit aucun signal visible et la fonctionnalité paraît inerte.
     *
     * Il n'ACTIVE jamais rien : la bascule reste un acte confirmé par mot de passe, réservé au
     * Directeur (docs/Volume_7_Security.md §16). Le badge n'y donne accès que par un lien vers
     * l'onglet Paramètres › Années ; pour les autres rôles il est purement informatif.
     *
     * GET /school-years est ouvert à tous les rôles de l'école : chacun voit donc le contexte, seul le
     * Directeur peut le changer.
     */
    Alpine.data('activeSchoolYearBadge', () => ({
        label: null,
        loaded: false,
        isDirecteur: window.auth.role === 'Directeur',

        async init() {
            // Une session sans école (Super Admin plateforme) n'a pas d'année active : GET /school-years
            // lui renverrait une liste vide. On n'affiche donc rien plutôt qu'un badge « Aucune ».
            if (!window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') {
                this.loaded = true;
                return;
            }

            try {
                const years = await window.api.get('/school-years');
                const active = Array.isArray(years) ? years.find((y) => y.isActive) : null;
                this.label = active ? active.label : null;
            } catch {
                // Confort d'affichage : un indicateur ne doit jamais bloquer l'écran. En cas d'échec,
                // il reste simplement absent — les données, elles, viennent toujours de l'API gardée.
                this.label = null;
            } finally {
                this.loaded = true;
            }
        }
    }));
});
