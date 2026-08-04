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

    /** Format d'erreur normalisé (docs/Volume_4_API_Design.md §0.4), ou corps vide pour un 401 du middleware JWT. */
    async toError(response) {
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
    },

    get(endpoint) { return this.request(endpoint, 'GET'); },
    post(endpoint, body) { return this.request(endpoint, 'POST', body); },
    put(endpoint, body) { return this.request(endpoint, 'PUT', body); },
    patch(endpoint, body) { return this.request(endpoint, 'PATCH', body); },
    delete(endpoint) { return this.request(endpoint, 'DELETE'); },
    /** Upload multipart (FormData) — même robustesse (renouvellement de jeton, 401, erreurs) que post(). */
    upload(endpoint, formData) { return this.request(endpoint, 'POST', formData); }
};
