/**
 * Client HTTP de l'API — dépend de auth.js, à charger avant lui.
 *
 * Sa raison d'être : une session ne doit jamais se terminer brutalement pendant une saisie. L'access
 * token ne vit que 15 minutes ; ce client le renouvelle de façon transparente (ticket JGK-A04), et
 * l'appelant n'a rien à savoir de tout cela — il fait api.get(...) et reçoit ses données.
 */

// Ticket JGK-I04 : point d'atterrissage unique quand SubscriptionAwaitingPaymentMiddleware bloque un
// appel. Le JWT ne voyage jamais sur une navigation classique (localStorage, pas de cookie) — c'est
// donc ICI, au premier appel d'API d'une page bloquée, que la redirection peut réellement se décider,
// jamais côté serveur au moment du rendu de la page (voir le commentaire de classe du middleware).
const SUBSCRIPTION_RESTRICTED_PATH = '/abonnement/paiement';

/**
 * Codes HTTP qui décrivent une indisponibilité PASSAGÈRE de l'infrastructure, pas un refus : un
 * reverse-proxy qui n'a pas encore de backend prêt (502), un redémarrage applicatif (503), une
 * requête coupée en amont (504). Un 4xx n'est jamais rejoué — le serveur a compris et a dit non.
 */
const TRANSIENT_HTTP_STATUSES = [502, 503, 504];

window.api = {
    baseUrl: '/api/v1',

    // 3 tentatives = 2 reprises, soit ~1,6 s de patience au pire avant de rendre la main. Au-delà,
    // l'utilisateur croit l'écran figé et recharge la page — ce qui annule le bénéfice.
    retryMaxAttempts: 3,
    retryBaseDelayMs: 400,

    async request(endpoint, method = 'GET', body = null) {
        // Renouvellement PRÉVENTIF : si l'on sait déjà que le jeton est périmé, inutile de dépenser
        // un aller-retour pour se faire répondre 401.
        if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
            await this.refreshOrRedirect();
        }

        let response = await this.sendWithRetry(endpoint, method, body);

        // Filet de sécurité : le jeton a pu être révoqué côté serveur, ou l'horloge du poste être
        // décalée au point que le renouvellement préventif n'ait pas vu venir l'expiration. Une seule
        // reprise — si le second appel échoue encore, insister ne ferait que boucler.
        if (response.status === 401 && window.auth.isAuthenticated()) {
            await this.refreshOrRedirect();
            response = await this.sendWithRetry(endpoint, method, body);
        }

        if (response.status === 401) {
            window.auth.redirectToLogin();
            throw await this.toError(response);
        }

        if (!response.ok) {
            const error = await this.toError(response);

            // Ticket JGK-I04 : un 403 "ordinaire" (rôle insuffisant) ne doit PAS rediriger — seul ce
            // code précis, posé par SubscriptionAwaitingPaymentMiddleware, déclenche la redirection.
            if (error.code === 'SUBSCRIPTION_AWAITING_PAYMENT'
                && window.location.pathname !== SUBSCRIPTION_RESTRICTED_PATH) {
                window.location.assign(SUBSCRIPTION_RESTRICTED_PATH);
            }

            throw error;
        }

        if (response.status === 204) return null;
        return await response.json();
    },

    /**
     * Reprise automatique des défaillances RÉSEAU passagères — le vrai quotidien d'une connexion
     * mobile sénégalaise : une requête part pendant un micro-basculement d'antenne et meurt seule,
     * alors que la suivante, 400 ms plus tard, passe sans problème.
     *
     * ┌─ RÈGLE ABSOLUE ────────────────────────────────────────────────────────────────────────┐
     * │ Seules les LECTURES (GET) sont rejouées. Jamais un POST/PUT/PATCH/DELETE.               │
     * └────────────────────────────────────────────────────────────────────────────────────────┘
     *
     * Un `fetch` qui échoue ne dit PAS si le serveur a traité la requête : la coupure peut être
     * survenue sur la réponse, la transaction étant déjà validée. Rejouer un POST /payments, c'est
     * donc encaisser deux fois le même versement ; rejouer une inscription, c'est consommer deux
     * matricules (AGENTS.md règle 3) pour un seul élève. Aucune reprise automatique ne peut
     * distinguer ces cas — c'est précisément ce qu'un identifiant d'idempotence serveur résoudrait,
     * et il n'en existe pas. L'écriture échouée remonte donc telle quelle à l'écran, qui conserve la
     * saisie (form-draft.js) et laisse l'utilisateur décider de renvoyer.
     */
    isRetryable(method) {
        return String(method).toUpperCase() === 'GET';
    },

    /** Palier exponentiel + gigue : deux onglets coupés ensemble ne repartent pas à la même seconde. */
    backoffDelayMs(attempt) {
        return (this.retryBaseDelayMs * Math.pow(2, attempt - 1)) + Math.floor(Math.random() * 100);
    },

    delay(ms) {
        return new Promise((resolve) => setTimeout(resolve, ms));
    },

    async sendWithRetry(endpoint, method, body) {
        const maxAttempts = this.isRetryable(method) ? this.retryMaxAttempts : 1;

        for (let attempt = 1; ; attempt++) {
            const isLastAttempt = attempt >= maxAttempts;

            try {
                const response = await this.send(endpoint, method, body);

                if (!isLastAttempt && TRANSIENT_HTTP_STATUSES.includes(response.status)) {
                    await this.delay(this.backoffDelayMs(attempt));
                    continue;
                }

                return response;
            } catch (error) {
                // `navigator.onLine` à false : le poste est franchement déconnecté, insister ne fait
                // que retarder le message d'erreur que l'utilisateur doit voir tout de suite.
                if (isLastAttempt || error.code !== 'NETWORK_OFFLINE' || !navigator.onLine) {
                    throw error;
                }

                await this.delay(this.backoffDelayMs(attempt));
            }
        }
    },

    async send(endpoint, method, body) {
        if (!navigator.onLine) {
            const error = new Error("📡 Connexion réseau indisponible. Votre saisie a été sauvegardée en mémoire sur votre navigateur. Veuillez retenter l'envoi dès le rétablissement de la connexion.");
            error.code = 'NETWORK_OFFLINE';
            error.status = 0;
            throw error;
        }

        const headers = {};
        const token = window.auth.accessToken;

        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        // FormData (upload de fichier) : le navigateur doit fixer lui-même le Content-Type avec sa
        // frontière multipart — l'imposer ici casserait le décodage côté serveur.
        const isFormData = body instanceof FormData;
        if (!isFormData) {
            headers['Content-Type'] = 'application/json';
        }

        try {
            return await fetch(`${this.baseUrl}${endpoint}`, {
                method,
                headers,
                credentials: 'same-origin',
                body: isFormData ? body : (body ? JSON.stringify(body) : undefined)
            });
        } catch (fetchErr) {
            if (!navigator.onLine || fetchErr.name === 'TypeError' || (fetchErr.message && fetchErr.message.includes('Failed to fetch'))) {
                const error = new Error("📡 Connexion au serveur interrompue pendant l'envoi. Votre saisie est conservée en mémoire dans le navigateur. Veuillez retenter dès que le réseau est rétabli.");
                error.code = 'NETWORK_OFFLINE';
                error.status = 0;
                throw error;
            }
            throw fetchErr;
        }
    },

    /**
     * Le renouvellement a échoué : le refresh token est expiré, révoqué, ou a été rejoué et le
     * serveur a coupé toute la famille. Il n'y a plus de session à sauver — on renvoie l'utilisateur
     * vers la connexion, en gardant la page courante pour l'y ramener ensuite.
     */
    async refreshOrRedirect() {
        try {
            await window.auth.refresh();
        } catch (error) {
            window.auth.redirectToLogin();
            throw error;
        }
    },

    /**
     * Traduit une erreur d'API en messages affichables SOUS LES CHAMPS du formulaire.
     *
     * L'API renvoie `details` sous la forme d'un DICTIONNAIRE { "FullName": ["Le nom est
     * obligatoire."] } — ExceptionHandlingMiddleware y place le dictionnaire Errors de
     * FluentValidation. Les formulaires attendaient jusqu'ici un TABLEAU de { field, reason } : la
     * condition Array.isArray() échouait donc toujours, et chaque erreur de saisie tombait dans le
     * message générique « global ». Aucune erreur ne s'est jamais affichée sous son champ.
     *
     * Les clés sont mises en minuscules pour correspondre aux noms attendus par les vues
     * (createErrors.fullname, createErrors.classroomid…).
     */
    toFieldErrors(error, fallbackMessage) {
        // Déjà porté par la modale universelle « Accès refusé » (access-denied.js) : aucun message
        // local, sinon il s'affiche en double derrière la modale.
        if (error && error.handledGlobally) return {};

        const details = error && error.details;

        if (details && typeof details === 'object' && !Array.isArray(details)) {
            const errors = {};

            Object.entries(details).forEach(([field, messages]) => {
                errors[field.toLowerCase()] = Array.isArray(messages) ? messages[0] : String(messages);
            });

            if (Object.keys(errors).length > 0) return errors;
        }

        // Pas de détail par champ : conflit (409), règle métier, panne réseau… → message global.
        return { global: (error && error.message) || fallbackMessage };
    },

    /**
     * Traduit une erreur d'API en UNE phrase à afficher dans un bandeau.
     *
     * À utiliser partout où un écran montre un bandeau unique plutôt que des erreurs sous les champs.
     * `error.message` seul ne suffit pas : sur un 422, le format normalisé
     * (docs/Volume_4_API_Design.md §0.4) place dans `message` la phrase de service « Une ou plusieurs
     * erreurs de validation se sont produites. » et garde l'explication RÉELLE dans `details`. Les
     * écrans qui affichaient `err.message` montraient donc la phrase de service et jetaient
     * l'explication — l'utilisateur apprenait qu'il y avait une erreur, jamais laquelle.
     *
     * Les détails sont joints par un espace : un même champ peut porter plusieurs reproches, et les
     * cacher tous sauf un obligerait l'utilisateur à corriger en plusieurs allers-retours.
     */
    toMessage(error, fallbackMessage) {
        // Déjà affiché par la modale universelle « Accès refusé » (access-denied.js) : on ne renvoie
        // rien, pour que le bandeau/toast de l'écran ne double pas la modale (« '' » est falsy, donc
        // x-show="error" et toast.error('') restent silencieux).
        if (error && error.handledGlobally) return '';

        const details = error && error.details;

        if (details && typeof details === 'object' && !Array.isArray(details)) {
            const phrases = Object.values(details)
                .flatMap(v => (Array.isArray(v) ? v : [v]))
                .map(v => String(v).trim())
                .filter(Boolean);

            if (phrases.length > 0) return phrases.join(' ');
        }

        return (error && error.message) || fallbackMessage;
    },

    /** Format d'erreur normalisé (docs/Volume_4_API_Design.md §0.4), ou corps vide pour un 401 du middleware JWT. */
    async toError(response) {
        const payload = await response.json().catch(() => null);

        if (payload && payload.message) {
            const error = new Error(payload.message);
            error.code = payload.code;
            error.details = payload.details;
            error.status = response.status;

            // Refus de PORTÉE de rôle (ForbiddenException serveur → code FORBIDDEN) : présenté par la
            // modale universelle « Accès refusé » (access-denied.js), identique sur tous les écrans,
            // plutôt que par le bandeau rouge de chacun. `handledGlobally` dit à toMessage()/
            // toFieldErrors() de ne rien renvoyer, pour ne pas doubler la modale. Le 403 d'abonnement
            // impayé (SUBSCRIPTION_AWAITING_PAYMENT) a son propre code et n'est pas concerné.
            if (response.status === 403 && payload.code === 'FORBIDDEN') {
                error.handledGlobally = true;
                window.dispatchEvent(new CustomEvent('sama:access-denied', {
                    detail: { message: payload.message }
                }));
            }

            return error;
        }

        const fallback = new Error(`Erreur HTTP ${response.status}`);
        fallback.status = response.status;
        return fallback;
    },

    get(endpoint) { return this.request(endpoint, 'GET'); },
    post(endpoint, body) { return this.request(endpoint, 'POST', body); },
    put(endpoint, body) { return this.request(endpoint, 'PUT', body); },
    patch(endpoint, body) { return this.request(endpoint, 'PATCH', body); },
    delete(endpoint) { return this.request(endpoint, 'DELETE'); },

    /**
     * Récupère la TOTALITÉ d'une collection paginée (`{ items, totalCount, page, pageSize }`), en
     * enchaînant les pages jusqu'à `totalCount`.
     *
     * Pourquoi ce helper existe : les validateurs serveur plafonnent `pageSize` à 100
     * (GetStudentsQueryValidator.MaxPageSize & consorts) — ce plafond est la seule chose qui empêche
     * un client de dicter la taille de la réponse, il ne doit PAS être relevé. Or plusieurs écrans
     * demandaient `pageSize=1000` pour remplir un menu déroulant : la requête partait en 422
     * VALIDATION_ERROR et le sélecteur restait vide, SANS message (bug constaté sur /teachers le
     * 27/08/2026, puis sur les trois écrans Surveillant — billets, discipline, convocations).
     *
     * Le repli « demander seulement 100 » n'est pas acceptable pour les élèves : un établissement
     * sénégalais dépasse couramment le millier (cf. GetStudentsQuery), et l'écran aurait alors
     * silencieusement masqué les élèves au-delà du centième — un bug invisible, pire que le bug
     * visible qu'il remplace.
     *
     * `maxPages` est un garde-fou anti-boucle (60 × 100 = 6 000 lignes) : au-delà, un menu déroulant
     * n'est plus le bon composant, il faut une recherche serveur (cf. le widget de /caisse).
     */
    async getAllPages(endpoint, options) {
        const pageSize = (options && options.pageSize) || 100;
        const maxPages = (options && options.maxPages) || 60;
        const separator = endpoint.includes('?') ? '&' : '?';
        const all = [];

        for (let page = 1; page <= maxPages; page++) {
            const data = await this.get(`${endpoint}${separator}page=${page}&pageSize=${pageSize}`);
            const items = (data && data.items) || [];
            all.push(...items);

            // Page vide => plus rien à lire (borne sûre même si totalCount venait à manquer).
            if (items.length === 0) break;

            const total = data && typeof data.totalCount === 'number' ? data.totalCount : all.length;
            if (all.length >= total) break;
        }

        return all;
    },
    /** Upload multipart (FormData) — même robustesse (renouvellement de jeton, 401, erreurs) que post(). */
    upload(endpoint, formData) { return this.request(endpoint, 'POST', formData); },

    /**
     * Écriture résiliente (ticket JGK-L03) : câble `window.networkGuard.submitWithRetry` (JGK-L02)
     * sur une méthode d'écriture, au lieu du simple `send()` non rejoué de `request()`. Réservé aux
     * écrans qui en ont explicitement besoin (Caisse, Pointage) — les autres écritures du produit
     * gardent volontairement le comportement « échec immédiat + brouillon local » de `post()`
     * (voir le commentaire de `isRetryable` ci-dessus).
     *
     * `config.onStateChange` est transmis tel quel à `submitWithRetry` : 'sending' | 'retrying' |
     * 'done' | 'failed'. Contrairement à `request()`, AUCUNE reprise sur 401 n'est tentée ICI — un
     * jeton expiré pendant une coupure réseau est un cas assez rare pour ne pas complexifier la seule
     * fonction dont la propriété recherchée est justement la simplicité de son chemin d'erreur.
     */
    async requestWithRetry(endpoint, method, body, config = {}) {
        if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
            await this.refreshOrRedirect();
        }

        const headers = { 'Content-Type': 'application/json' };
        const token = window.auth.accessToken;
        if (token) headers['Authorization'] = `Bearer ${token}`;

        const options = {
            method,
            headers,
            credentials: 'same-origin',
            body: body ? JSON.stringify(body) : undefined
        };

        let response;
        try {
            response = await window.networkGuard.submitWithRetry(`${this.baseUrl}${endpoint}`, options, config);
        } catch {
            // maxAttempts atteint sans qu'aucune tentative n'ait joint le serveur : même message et
            // même code que send() pour rester interprétable par toMessage()/toFieldErrors().
            const error = new Error("📡 Connexion au serveur interrompue après plusieurs tentatives. Rien n'a été enregistré tant que la connexion n'est pas rétablie.");
            error.code = 'NETWORK_OFFLINE';
            error.status = 0;
            throw error;
        }

        if (response.status === 401) {
            window.auth.redirectToLogin();
            throw await this.toError(response);
        }

        if (!response.ok) throw await this.toError(response);
        if (response.status === 204) return null;
        return await response.json();
    },

    postWithRetry(endpoint, body, config) { return this.requestWithRetry(endpoint, 'POST', body, config); }
};
