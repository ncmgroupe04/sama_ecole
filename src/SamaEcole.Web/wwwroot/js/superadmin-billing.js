/**
 * Console Super Admin — Abonnements & Facturation (refonte UI/UX).
 *
 * TODO BACKEND : `GET /api/v1/admin/platform/subscriptions` n'existe pas. Contrat attendu : un
 * tableau de { schoolId, schoolName, plan (SubscriptionPlan), status (SubscriptionStatus),
 * expiresAt (ISO date|null), lastPaymentAmountXof, lastPaymentAt }. Comme pour le tableau de bord
 * (voir superadmin-dashboard.js), Subscription/SubscriptionPayment restent soumis à la RLS par
 * tenant : une vraie implémentation demande sa propre conception d'accès plateforme, pas un simple
 * ajout de endpoint.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminBilling', () => ({
        subscriptions: [],
        isLoading: false,
        isDemoData: false,
        error: null,
        searchQuery: '',
        statusFilter: 'All',

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.subscriptions = await window.api.get('/admin/platform/subscriptions');
                this.isDemoData = false;
            } catch (err) {
                if (err.status === 404) {
                    this.subscriptions = demoSubscriptions();
                    this.isDemoData = true;
                } else {
                    this.error = err.message || 'Erreur lors du chargement des abonnements.';
                }
            } finally {
                this.isLoading = false;
            }
        },

        get filteredSubscriptions() {
            const query = this.searchQuery.trim().toLowerCase();
            return this.subscriptions.filter((sub) => {
                const matchesStatus = this.statusFilter === 'All' || sub.status === this.statusFilter;
                const matchesQuery = !query || sub.schoolName.toLowerCase().includes(query);
                return matchesStatus && matchesQuery;
            });
        },

        get kpiTiles() {
            const active = this.subscriptions.filter((s) => s.status === 'Active').length;
            const awaiting = this.subscriptions.filter((s) => s.status === 'AwaitingPayment').length;
            const dueSoon = this.subscriptions.filter((s) => this.isDueSoon(s.expiresAt)).length;
            const suspended = this.subscriptions.filter((s) => s.status === 'Suspended').length;

            return [
                { label: 'Abonnements actifs', value: active, valueClass: 'text-emerald-400' },
                { label: 'En attente de paiement', value: awaiting, valueClass: 'text-sky-400' },
                { label: 'Échéances < 7 jours', value: dueSoon, valueClass: 'text-amber-400' },
                { label: 'Suspendus', value: suspended, valueClass: 'text-rose-400' }
            ];
        },

        isDueSoon(expiresAt) {
            if (!expiresAt) return false;
            const days = (new Date(expiresAt) - new Date()) / (1000 * 60 * 60 * 24);
            return days >= 0 && days <= 7;
        },

        planLabel(plan) {
            return { Primaire: 'Primaire', Standard: 'Standard', Premium: 'Premium' }[plan] || plan;
        },

        statusLabel(status) {
            return {
                AwaitingPayment: 'En attente', Active: 'Actif', Suspended: 'Suspendu', ReadOnly: 'Lecture seule'
            }[status] || status;
        },

        statusClasses(status) {
            return {
                Active: 'bg-emerald-500/15 text-emerald-400 ring-1 ring-inset ring-emerald-500/20',
                AwaitingPayment: 'bg-sky-500/15 text-sky-400 ring-1 ring-inset ring-sky-500/20',
                Suspended: 'bg-rose-500/15 text-rose-400 ring-1 ring-inset ring-rose-500/20',
                ReadOnly: 'bg-zinc-800 text-zinc-300'
            }[status] || 'bg-zinc-800 text-zinc-300';
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleDateString('fr-FR');
        },

        formatXof(amount) {
            if (!amount) return '—';
            return new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'XOF', maximumFractionDigits: 0 }).format(amount);
        }
    }));
});

function demoSubscriptions() {
    const names = ['Groupe Scolaire Diamniadio', 'Institut Sainte-Marie', 'École Les Baobabs', 'Complexe Scolaire Teranga', 'Lycée Moderne Thiès', 'École Al Azhar'];
    const plans = ['Primaire', 'Standard', 'Premium'];
    const statuses = ['Active', 'Active', 'AwaitingPayment', 'Suspended', 'Active', 'ReadOnly'];

    return names.map((schoolName, i) => {
        const daysOffset = [45, 4, -2, 12, 60, 25][i];
        const expiresAt = new Date(Date.now() + daysOffset * 86400000).toISOString();
        return {
            schoolId: `demo-${i}`,
            schoolName,
            plan: plans[i % plans.length],
            status: statuses[i],
            expiresAt: statuses[i] === 'AwaitingPayment' ? null : expiresAt,
            lastPaymentAmountXof: statuses[i] === 'AwaitingPayment' ? null : 25000 + i * 15000
        };
    });
}
