/**
 * Onglet Facturation & Historique (Paramètres, ticket JGK-I07) — lecture seule : le statut de
 * l'abonnement et l'historique des paiements ne se modifient jamais depuis cet écran (seul le webhook
 * signé de l'agrégateur confirme un paiement, AGENTS.md règle #11).
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
