/**
 * Modale UNIVERSELLE « Mise à jour de votre abonnement » — montée une seule fois par _Layout (_SubscriptionChangeModal.cshtml).
 *
 * Le profil, les cycles gérés et les modules NON souscrits sont VERROUILLÉS : le serveur intercepte toute modification directe
 * (409 SUBSCRIPTION_CHANGE_REQUIRED, rien n'est écrit) et renvoie un APERÇU — ce qui changerait, le tarif ajusté, et le corps de
 * la demande à envoyer. window.api ouvre alors CETTE modale, identique sur tous les écrans, au lieu d'un bandeau rouge par écran,
 * et marque l'erreur `handledGlobally` pour que toMessage()/toFieldErrors() ne la redisent pas.
 *
 * Le Directeur y envoie la demande (POST /tenant-subscription/change-requests) ; elle ne change RIEN tant que le Super Admin ne
 * l'a pas validée. Le bandeau de quota ouvre la même modale (`requestChange`) pour passer à la tranche supérieure.
 *
 * Le pont passe par un évènement `window` : api.js est un script classique exécuté avant Alpine, il ne peut pas toucher au
 * store au moment où il tourne (même mécanisme que access-denied.js).
 */
document.addEventListener('alpine:init', () => {
    Alpine.store('subscriptionChange', {
        open: false,
        message: null,
        preview: null,
        note: '',
        isLoading: false,
        isSending: false,
        error: null,
        sentRequest: null,

        show(detail) {
            const preview = (detail && detail.preview) || null;
            this.preview = preview;
            this.message = (detail && detail.message) || (preview && preview.message) || null;
            this.note = '';
            this.error = null;
            this.sentRequest = null;
            this.open = true;
        },

        close() {
            this.open = false;
        },

        /** Seul le Directeur envoie une demande ; les autres rôles voient l'aperçu sans pouvoir l'envoyer. */
        get isDirector() {
            return !!window.auth && window.auth.role === 'Directeur';
        },

        get canSend() {
            return this.isDirector && !!this.preview && this.preview.canRequest === true && !this.isSending && !this.sentRequest;
        },

        get hasProblems() {
            return !!this.preview && Array.isArray(this.preview.problems) && this.preview.problems.length > 0;
        },

        get isOnQuote() {
            return !!this.preview && !!this.preview.target && this.preview.target.requiresQuote === true;
        },

        /** Écart de tarif connu (les deux tarifs comparables). */
        get hasPriceDifference() {
            return !!this.preview && this.preview.differenceMonthlyXof !== null && this.preview.differenceMonthlyXof !== undefined;
        },

        /** Nouveau tarif connu, mais l'ancien non comparable (école encore sur l'ancienne grille). */
        get hasTargetPriceOnly() {
            return !!this.preview && !this.hasPriceDifference && !this.isOnQuote
                && !!this.preview.target && this.preview.target.known === true && !this.preview.isDowngrade;
        },

        formatXof(amount) {
            if (amount === null || amount === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'XOF', maximumFractionDigits: 0 }).format(amount);
        },

        /** « +5 000 F CFA » / « −2 000 F CFA » : le signe fait lire une montée ou une baisse. */
        formatSigned(amount) {
            if (amount === null || amount === undefined) return '—';
            return (amount > 0 ? '+' : amount < 0 ? '−' : '') + this.formatXof(Math.abs(amount));
        },

        /**
         * Ouvre la modale pour un changement choisi ailleurs (bandeau de quota : « passer à la tranche X »). L'aperçu vient du
         * serveur — le tarif n'est jamais calculé ici.
         */
        async requestChange(change) {
            this.isLoading = true;

            try {
                const preview = await window.api.post('/tenant-subscription/change-requests/preview', change);
                this.show({ preview, message: preview.message });
            } catch (err) {
                // Hors modale (elle n'est pas encore ouverte) : on l'ouvre avec le seul message d'erreur.
                this.show({ preview: null, message: null });
                this.error = window.api.toMessage(err, "Impossible de préparer la demande. Réessayez.");
            } finally {
                this.isLoading = false;
            }
        },

        async send() {
            if (!this.canSend) return;

            this.isSending = true;
            this.error = null;

            try {
                // Le corps de la demande est celui que le serveur a renvoyé dans l'aperçu, tel quel.
                this.sentRequest = await window.api.post('/tenant-subscription/change-requests', {
                    change: this.preview.request,
                    note: this.note && this.note.trim() ? this.note.trim() : null
                });
            } catch (err) {
                this.error = window.api.toMessage(err, "Impossible d'envoyer la demande. Réessayez.");
            } finally {
                this.isSending = false;
            }
        }
    });

    window.addEventListener('sama:subscription-change', (event) => {
        Alpine.store('subscriptionChange').show(event.detail);
    });
});
