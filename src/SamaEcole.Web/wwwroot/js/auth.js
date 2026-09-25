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

    const DAY_INDEX = { Sunday: 0, Monday: 1, Tuesday: 2, Wednesday: 3, Thursday: 4, Friday: 5, Saturday: 6 };

    /** Noms de jours de l'API → index DayOfWeek, ordre conservé ; défaut lundi → samedi si absent/vide. */
    window.dayIndexes = (names) => {
        const list = Array.isArray(names) ? names.map((n) => DAY_INDEX[n]).filter((i) => i !== undefined) : [];
        return list.length > 0 ? list : [1, 2, 3, 4, 5, 6];
    };

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

    /**
     * Message affichable quand la réponse n'a pas le format normalisé — un 401 du middleware JWT n'a
     * aucun corps, et un échec de model-binding renvoie l'erreur automatique d'ASP.NET Core, jamais un
     * `message` exploitable. Sans traduction, l'utilisateur verrait littéralement « Erreur HTTP 400 ».
     */
    function httpFallbackMessage(status) {
        if (status === 401) return 'Identifiants incorrects, ou session expirée. Reconnectez-vous.';
        if (status === 403) return "Vous n'avez pas les droits nécessaires pour effectuer cette action.";
        if (status >= 500) return 'Le service rencontre une difficulté technique. Réessayez dans quelques instants.';
        return 'Une erreur inattendue est survenue. Réessayez, et contactez le support si le problème persiste.';
    }

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

        const fallback = new Error(httpFallbackMessage(response.status));
        fallback.status = response.status;
        return fallback;
    }

    /** Lit les claims de l'access token sans vérifier la signature — l'affichage seulement, jamais une décision de sécurité. */
    function readClaims(token) {
        if (!token) return null;

        try {
            const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
            // atob() rend une chaîne d'OCTETS : sans re-décoder en UTF-8, un claim accentué
            // (« Mbacké ») ressort en mojibake (« MbackÃ© ») dans la barre latérale et la topbar.
            const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
            const json = new TextDecoder('utf-8').decode(bytes);
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

        /**
         * Commodité d'affichage réutilisable depuis N'IMPORTE QUEL sous-arbre du DOM (header,
         * sidebar…) sans dépendre de la portée Alpine d'un composant particulier — comme tout
         * `x-show` basé sur le rôle, ce n'est jamais une mesure de sécurité, seule l'API protège.
         */
        canView(roles) {
            return roles.includes(this.role);
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
    const EMAIL_REGEX = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;

    /** Durée de blocage lisible : `retryAfterSeconds` vient de AccountLockedException (voir 429). */
    function formatRetryAfter(seconds) {
        if (seconds < 60) return `${seconds} s`;
        const minutes = Math.ceil(seconds / 60);
        if (minutes < 60) return `${minutes} min`;
        return `${Math.ceil(minutes / 60)} h`;
    }

    Alpine.data('loginForm', () => ({
        email: '',
        password: '',
        showPassword: false,
        isSubmitting: false,
        error: null,
        errors: {},

        /** Validation CLIENT immédiate — forme uniquement (format d'e-mail, champs requis) : ni la
         * robustesse du mot de passe ni son existence ne sont vérifiées ici, ça reste l'affaire du
         * serveur (LoginCommandHandler), seul à savoir si les identifiants correspondent à un compte. */
        validate() {
            const errors = {};

            if (!this.email) {
                errors.email = "L'adresse e-mail est obligatoire.";
            } else if (!EMAIL_REGEX.test(this.email)) {
                errors.email = "Le format de l'adresse e-mail est invalide.";
            }

            if (!this.password) {
                errors.password = 'Le mot de passe est obligatoire.';
            }

            this.errors = errors;
            return Object.keys(errors).length === 0;
        },

        /** Validation DYNAMIQUE au départ du champ e-mail (x-on:blur) : nettoie puis revalide. */
        touchEmail() {
            this.email = this.email.trim();
            this.validate();
        },

        async submit() {
            this.error = null;
            this.errors = {};

            // Le remplissage automatique du navigateur (gestionnaire de mots de passe, suggestion de
            // l'omnibox) pose la valeur dans le DOM sans toujours déclencher l'évènement `input` dont
            // x-model dépend pour se synchroniser : this.email/this.password pouvaient alors rester
            // vides malgré des champs visiblement remplis, un premier clic échouait silencieusement et
            // DÉSACTIVAIT le bouton (errors se remplissait), qui ne se réactivait qu'après un blur
            // manuel resynchronisant x-model. On relit donc la valeur RÉELLE du DOM juste avant de
            // valider — toujours exacte, qu'elle vienne d'une frappe ou d'un remplissage automatique.
            if (this.$refs && this.$refs.email) this.email = this.$refs.email.value;
            if (this.$refs && this.$refs.password) this.password = this.$refs.password.value;

            // Espaces superflus retirés de l'e-mail uniquement : un espace dans le mot de passe est un
            // caractère comme un autre, le rogner changerait le secret saisi par l'utilisateur.
            this.email = this.email.trim();

            if (!this.validate()) {
                return;
            }

            this.isSubmitting = true;

            try {
                await window.auth.login(this.email, this.password);
                window.location.assign(window.auth.landingUrl());
            } catch (err) {
                if (err.code === 'ACCOUNT_LOCKED') {
                    // 429 (blocage progressif anti-force-brute) : `details` porte { retryAfterSeconds },
                    // pas un dictionnaire de champs — le laisser passer par toFieldErrors le ferait
                    // traiter comme une erreur de champ « retryafterseconds » muette à l'écran (aucun
                    // champ n'y est lié), et le bandeau global resterait vide. Cas à part, explicite.
                    const seconds = err.details && err.details.retryAfterSeconds;
                    this.error = seconds
                        ? `Compte temporairement verrouillé. Réessayez dans ${formatRetryAfter(seconds)}.`
                        : err.message;
                } else {
                    // Le serveur renvoie le MÊME message pour un e-mail inconnu et un mot de passe faux
                    // (LoginCommandHandler), sans `details` par champ (voir InvalidCredentialsException) :
                    // toFieldErrors retombe donc sur errors.global, jamais sur errors.email/errors.password
                    // — impossible d'en déduire lequel des deux est en cause, ce qui rouvrirait
                    // l'énumération de comptes que le serveur évite déjà.
                    this.errors = window.api.toFieldErrors(err, 'Connexion impossible. Vérifiez votre réseau et réessayez.');
                    this.error = this.errors.global || null;
                }
                this.password = '';
            } finally {
                this.isSubmitting = false;
            }
        }
    }));

    /**
     * Carte de profil de la barre latérale (_Layout.cshtml) : nom + rôle lus dans le JWT, ouvre un
     * petit menu (isOpen) avec « Changer mon mot de passe » (délègue au store Alpine
     * changePasswordModal, wwwroot/js/change-password.js) et « Se déconnecter ».
     */
    Alpine.data('sessionMenu', () => ({
        email: window.auth.email,
        name: window.auth.name,
        role: window.auth.role,
        // « Changer mon e-mail » (POST /auth/change-email) est réservé au Directeur et au Super Admin
        // (AuthController.ChangeEmail) — décision produit du 20/09/2026 : un compte que le Directeur
        // crée lui-même (Secrétariat, Finance, Enseignant, Surveillant) n'a pas à le solliciter pour
        // corriger son e-mail de connexion, seulement pour son mot de passe (ouvert à tous, ci-dessous).
        canChangeEmail: window.auth.role === 'Directeur' || window.auth.role === 'SuperAdmin',
        isOpen: false,
        logout: () => window.auth.logout()
    }));

    /**
     * Ouverture/fermeture du dropdown « Saut rapide » de la barre de navigation rapide
     * (_QuickNav.cshtml), visible sous `lg:`. La liste des modules, les gardes de rôle/formule et
     * l'état actif sont rendus côté serveur (Razor) — ce composant ne porte que le booléen
     * d'ouverture, même précédent que sessionMenu ci-dessus.
     */
    Alpine.data('quickNavMenu', () => ({
        isOpen: false
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
     * Configuration d'établissement partagée (isPublicSchool, modules Pédagogie/Finance/Internat) —
     * AVANT sidebarNav ci-dessous, qui délègue désormais à ce store plutôt que de refaire son
     * propre GET /schools/current/settings. Sans ce store, chaque nouveau composant qui a besoin
     * de ces flags (sidebar, barre de navigation rapide…) rajouterait un appel réseau redondant à
     * chaque chargement de page — coûteux sur une connexion instable (voir Résilience réseau,
     * Centre d'aide). Un seul fetch, mutualisé, réentrant sans second appel.
     */
    Alpine.store('schoolConfig', {
        loaded: false,

        /**
         * Vrai pour les établissements publics sénégalais : Caisse, Finance et Dashboard financier
         * sont masqués dans la navigation (l'API reste accessible, c'est une contrainte d'affichage).
         * Chargé via GET /schools/current/settings — non bloquant : en cas d'erreur réseau, la
         * valeur reste false (mode Privé par défaut, accès complet Finance conservé).
         */
        isPublicSchool: false,

        /**
         * Modules Pédagogie/Finance activés par le Directeur (Paramètres › Modules) — CONFORT
         * D'AFFICHAGE UNIQUEMENT, comme isPublicSchool ci-dessus : la vraie protection est
         * [RequireModule] côté serveur (403 MODULE_DISABLED). Chargés dans le même appel que
         * isPublicSchool ; par défaut à `true` (menu complet) tant que la réponse n'est pas arrivée
         * ou en cas d'erreur réseau — Pédagogie/Finance sont le socle métier, sûr par défaut.
         */
        pedagogyEnabled: true,
        financeEnabled: true,

        /**
         * Module Internat (Paramètres › Modules) — INVERSE de pedagogyEnabled/financeEnabled :
         * désactivé par défaut (SchoolSettingsDefaults.IsInternatEnabled = false), donc masqué tant
         * que la réponse n'est pas arrivée ou en cas d'erreur réseau (sûr par défaut = caché, pas
         * affiché, puisque le module est réservé/inerte tant que le Directeur ne l'a pas activé).
         */
        internatEnabled: false,

        /**
         * Jours OUVRÉS de l'établissement (DayOfWeek : dimanche = 0), dans l'ordre d'AFFICHAGE calculé par
         * le serveur (SchoolWeek.DisplayOrder). Défaut lundi → samedi tant que la réponse n'est pas
         * arrivée : la grille historique. CONFORT d'affichage — le vrai verrou est WorkingDayGuard (422).
         */
        workingDays: [1, 2, 3, 4, 5, 6],

        isWorkingDay(isoDate) {
            const [y, m, d] = String(isoDate).split('-').map(Number);
            if (!y || !m || !d) return true; // date illisible : on ne bloque pas, le serveur juge
            return this.workingDays.includes(new Date(y, m - 1, d).getDay());
        },

        // Réentrance : plusieurs composants (sidebar, barre de navigation rapide) appellent init()
        // sur le même store au boot — un seul fetch doit réellement partir.
        _initPromise: null,

        init() {
            if (!this._initPromise) this._initPromise = this._load();
            return this._initPromise;
        },

        async _load() {
            // Super Admin plateforme : pas d'école, pas de settings. On laisse les valeurs par défaut.
            if (!window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') {
                this.loaded = true;
                return;
            }
            try {
                const s = await window.api.get('/schools/current/settings');
                this.isPublicSchool = (s && s.typeEtablissement === 'Public');
                this.pedagogyEnabled = !s || s.isPedagogyEnabled !== false;
                this.financeEnabled = !s || s.isFinanceEnabled !== false;
                this.internatEnabled = !!s && s.isInternatEnabled === true;
                this.workingDays = window.dayIndexes(s && s.workingDays);
            } catch {
                // Non bloquant : en cas d'erreur réseau, la navigation reste complète pour
                // Pédagogie/Finance (socle métier, sûr par défaut) mais Internat reste masqué —
                // il n'y a rien de "sûr par défaut" à afficher pour un module réservé/inerte.
                this.isPublicSchool = false;
                this.pedagogyEnabled = true;
                this.financeEnabled = true;
                this.internatEnabled = false;
                this.workingDays = [1, 2, 3, 4, 5, 6];
            } finally {
                this.loaded = true;
            }
        }
    });

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
     *
     * isPublicSchool/pedagogyEnabled/financeEnabled/internatEnabled délèguent désormais au store
     * partagé `schoolConfig` (ci-dessus) plutôt que de les charger eux-mêmes : le balisage Razor de
     * la sidebar (x-show="... && pedagogyEnabled" etc.) continue de fonctionner à l'identique, ces
     * getters ne changent rien d'observable.
     */
    Alpine.data('sidebarNav', () => ({
        role: window.auth.role,

        get isPublicSchool() { return Alpine.store('schoolConfig').isPublicSchool; },
        get pedagogyEnabled() { return Alpine.store('schoolConfig').pedagogyEnabled; },
        get financeEnabled() { return Alpine.store('schoolConfig').financeEnabled; },
        get internatEnabled() { return Alpine.store('schoolConfig').internatEnabled; },

        init() {
            return Alpine.store('schoolConfig').init();
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

    /**
     * Pastille de RÉGIME de la barre supérieure — « Mode test » ou « Mode réel ». Elle ne disparaît
     * plus au passage en mode réel (demande du 15/09/2026) : le régime est le contexte de travail de
     * l'établissement, et il doit se lire d'un coup d'œil sur chaque écran, dans les deux sens. En
     * mode test, elle devient un AVERTISSEMENT orange (« MODE TEST : Cliquez ici pour passer en Mode
     * Réel ») : les données saisies sont des essais, et le Directeur est mené droit à la bascule
     * (Paramètres › Paramètres système › Passer en mode réel).
     *
     * Source unique : GET /schools/current/mode (isLive). N'ACTIVE rien — la bascule reste un acte
     * confirmé du Directeur. Non bloquante : en cas d'échec réseau on retombe sur `isLive`, le défaut
     * prudent (on ne crie pas « mode test » sans en être sûr).
     */
    Alpine.data('sandboxModeBadge', () => ({
        isLive: true, // défaut prudent : pas de pastille tant qu'on n'a pas confirmé le mode test
        loaded: false,

        // Seul le Directeur peut agir sur le régime : pour lui la pastille est un LIEN vers
        // Paramètres › Sécurité (Zone de danger), où le retour au mode test reste visible en
        // permanence. Pour les autres rôles elle reste purement informative.
        isDirecteur: window.auth.role === 'Directeur',

        async init() {
            if (!window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') {
                this.loaded = true;
                return;
            }
            try {
                // Lecture partagée avec la modale d'avertissement (school-mode-guard.js) : une requête.
                const mode = await window.schoolMode.get();
                this.isLive = !!(mode && mode.isLive);
            } catch {
                this.isLive = true;
            } finally {
                this.loaded = true;
            }
        }
    }));
});
