/**
 * Client HTTP de l'API — dépend de auth.js, à charger avant lui.
 *
 * Sa raison d'être : une session ne doit jamais se terminer brutalement pendant une saisie. L'access
 * token ne vit que 15 minutes ; ce client le renouvelle de façon transparente (ticket JGK-A04), et
 * l'appelant n'a rien à savoir de tout cela — il fait api.get(...) et reçoit ses données.
 */
window.api = {
    baseUrl: '/api/v1',

    async request(endpoint, method = 'GET', body = null) {
        // Renouvellement PRÉVENTIF : si l'on sait déjà que le jeton est périmé, inutile de dépenser
        // un aller-retour pour se faire répondre 401.
        if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
            await this.refreshOrRedirect();
        }

        let response = await this.send(endpoint, method, body);

        // Filet de sécurité : le jeton a pu être révoqué côté serveur, ou l'horloge du poste être
        // décalée au point que le renouvellement préventif n'ait pas vu venir l'expiration. Une seule
        // reprise — si le second appel échoue encore, insister ne ferait que boucler.
        if (response.status === 401 && window.auth.isAuthenticated()) {
            await this.refreshOrRedirect();
            response = await this.send(endpoint, method, body);
        }

        if (response.status === 401) {
            window.auth.redirectToLogin();
            throw await this.toError(response);
        }

        if (!response.ok) {
            throw await this.toError(response);
        }

        if (response.status === 204) return null;
        return await response.json();
    },

    async send(endpoint, method, body) {
        const headers = { 'Content-Type': 'application/json' };
        const token = window.auth.accessToken;

        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        return await fetch(`${this.baseUrl}${endpoint}`, {
            method,
            headers,
            credentials: 'same-origin',
            body: body ? JSON.stringify(body) : undefined
        });
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
    delete(endpoint) { return this.request(endpoint, 'DELETE'); }
};
