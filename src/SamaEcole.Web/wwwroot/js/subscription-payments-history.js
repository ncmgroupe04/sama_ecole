/**
 * Onglet Facturation & Historique (Paramètres, ticket JGK-I07) — le statut de l'abonnement et l'historique
 * des paiements ne se modifient jamais depuis cet écran (seul le webhook signé de l'agrégateur confirme un
 * paiement, AGENTS.md règle #11). Le bouton « Renouveler mon abonnement » ne fait que MENER à l'écran de paiement
 * (/abonnement/paiement) : rien ne se paie ni ne s'active ici.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('subscriptionPaymentsHistoryView', () => ({
        entries: [],
        isLoading: false,
        error: null,

        isDirecteur: window.auth.role === 'Directeur',

        page: 1,
        pageSize: 20,
        totalCount: 0,

        subscriptionStatus: null,
        subscriptionPlan: null,
        subscriptionExpiresAt: null,

        init() {
            if (this.isDirecteur) this.load();
        },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                const data = await window.api.get(`/subscriptions/${window.auth.schoolId}/payments?${params.toString()}`);
                this.entries = data.items || [];
                this.totalCount = data.totalCount || 0;
                this.subscriptionStatus = data.subscriptionStatus;
                this.subscriptionPlan = data.subscriptionPlan;
                this.subscriptionExpiresAt = data.subscriptionExpiresAt;
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors du chargement de l'historique des paiements.");
            } finally {
                this.isLoading = false;
            }
        },

        // --- Renouvellement ---------------------------------------------------------------------------
        // Écran de paiement existant (JGK-I05) ; `renouvellement=1` y adapte les textes (ce n'est pas un premier paiement).
        renewUrl: '/abonnement/paiement?renouvellement=1',
        renewalWarningDays: 30,

        /** Jours restants avant l'échéance (négatif = échue). Dates calendaires locales : pas de décalage d'heure d'été/fuseau. */
        daysUntilExpiry(now = new Date()) {
            if (!this.subscriptionExpiresAt) return null;
            const [y, m, d] = String(this.subscriptionExpiresAt).slice(0, 10).split('-').map(Number);
            const expiry = new Date(y, m - 1, d);
            const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
            return Math.round((expiry - today) / 86400000);
        },

        /** Lecture seule en base, ou échéance dépassée alors que le contrôle quotidien n'a pas encore basculé le statut. */
        get isExpired() {
            if (this.subscriptionStatus === 'ReadOnly') return true;
            const days = this.daysUntilExpiry();
            return this.subscriptionStatus === 'Active' && days !== null && days < 0;
        },

        get isExpiringSoon() {
            const days = this.daysUntilExpiry();
            return this.subscriptionStatus === 'Active' && days !== null && days >= 0 && days <= this.renewalWarningDays;
        },

        /** Seul le Directeur régularise l'abonnement ; Suspendu / En attente de paiement ne se règlent pas d'ici. */
        get canRenew() {
            return this.isDirecteur && (this.subscriptionStatus === 'Active' || this.subscriptionStatus === 'ReadOnly');
        },

        get renewalMessage() {
            if (this.isExpired) {
                return "Votre abonnement a expiré : l'accès est en lecture seule. Renouvelez-le pour retrouver toutes les fonctions.";
            }
            if (this.isExpiringSoon) {
                const days = this.daysUntilExpiry();
                const when = days === 0 ? "aujourd'hui" : days === 1 ? 'demain' : `dans ${days} jours`;
                return `Votre abonnement expire ${when}. Renouvelez-le dès maintenant : la période payée s'ajoute au temps restant, vous ne perdez aucun jour.`;
            }
            return "Votre abonnement est en cours. Vous pouvez le renouveler à l'avance : la période payée s'ajoute au temps restant.";
        },

        statusLabel(status) {
            return { Initiated: 'En cours', Confirmed: 'Confirmé', Failed: 'Échoué' }[status] || status;
        },

        statusBadgeVariant(status) {
            return {
                Initiated: 'status-badge-warning',
                Confirmed: 'status-badge-success',
                Failed: 'status-badge-danger'
            }[status] || 'status-badge-neutral';
        },

        subscriptionStatusLabel(status) {
            return {
                AwaitingPayment: 'En attente de paiement',
                Active: 'Actif',
                Suspended: 'Suspendu',
                ReadOnly: 'Lecture seule'
            }[status] || status;
        },

        subscriptionStatusBadgeVariant(status) {
            return {
                AwaitingPayment: 'status-badge-warning',
                Active: 'status-badge-success',
                Suspended: 'status-badge-danger',
                ReadOnly: 'status-badge-neutral'
            }[status] || 'status-badge-neutral';
        },

        methodLabel(method) {
            return { MobileMoney: 'Mobile Money', BankTransfer: 'Virement bancaire', Card: 'Carte' }[method] || method;
        },

        billingPeriodLabel(period) {
            return { Monthly: 'Mensuel', Yearly: 'Annuel' }[period] || period;
        },

        formatAmount(amount, currency) {
            return new Intl.NumberFormat('fr-FR').format(amount) + ' ' + (currency || 'XOF');
        },

        formatDate(iso) {
            if (!iso) return '—';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR') + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        },

        formatExpiresAt(dateOnly) {
            if (!dateOnly) return '—';
            return new Date(dateOnly).toLocaleDateString('fr-FR');
        }
    }));
});
