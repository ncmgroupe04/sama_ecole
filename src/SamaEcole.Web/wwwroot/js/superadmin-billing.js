/**
 * Console Super Admin — Abonnements & Facturation. Consomme le vrai `GET /admin/platform/subscriptions`
 * (PlatformController, migration AddPlatformSubscriptionsAndImpersonation : vue PostgreSQL
 * `v_platform_subscriptions`, security_invoker = false comme le tableau de bord). Le bouton « Relancer »
 * appelle `POST /admin/platform/subscriptions/{schoolId}/remind` : envoie un rappel par e-mail au
 * Directeur de l'établissement (SendSubscriptionReminderCommand) — n'écrit AUCUN paiement ni statut
 * d'abonnement (AGENTS.md règle #11), un simple aller-retour sans confirmation n'est donc pas destructif.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminBilling', () => ({
        subscriptions: [],
        isLoading: false,
        error: null,
        searchQuery: '',
        statusFilter: 'All',

        remindingSchoolId: null,
        remindedSchoolIds: {},
        remindError: null,

        grantAccessTarget: null,
        grantAccessForm: { plan: 'Standard', durationMonths: 1 },
        grantAccessError: null,
        isGranting: false,

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.subscriptions = await window.api.get('/admin/platform/subscriptions');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des abonnements.');
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
                { label: 'Abonnements actifs', value: active, valueClass: 'text-emerald-400', icon: 'checkmark-circle', iconBg: 'bg-emerald-500/15' },
                { label: 'En attente de paiement', value: awaiting, valueClass: 'text-sky-400', icon: 'clock', iconBg: 'bg-sky-500/15' },
                { label: 'Échéances < 7 jours', value: dueSoon, valueClass: 'text-amber-400', icon: 'alert-circle', iconBg: 'bg-amber-500/15' },
                { label: 'Suspendus', value: suspended, valueClass: 'text-rose-400', icon: 'block', iconBg: 'bg-rose-500/15' }
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
        },

        formatPeriodicValue(amount, period) {
            if (!amount) return '—';
            const formattedAmount = this.formatXof(amount);
            if (period === 'Monthly') return `${formattedAmount} / mois`;
            if (period === 'Yearly') return `${formattedAmount} / an`;
            return formattedAmount;
        },

        async remind(sub) {
            this.remindingSchoolId = sub.schoolId;
            this.remindError = null;
            try {
                await window.api.post(`/admin/platform/subscriptions/${sub.schoolId}/remind`);
                this.remindedSchoolIds[sub.schoolId] = true;
            } catch (err) {
                this.remindError = window.api.toMessage(err, "Erreur lors de l'envoi du rappel.");
            } finally {
                this.remindingSchoolId = null;
            }
        },

        openGrantAccess(sub) {
            this.grantAccessTarget = sub;
            this.grantAccessForm = { plan: sub.plan || 'Standard', durationMonths: 1 };
            this.grantAccessError = null;
        },

        closeGrantAccess() {
            this.grantAccessTarget = null;
        },

        async confirmGrantAccess() {
            this.isGranting = true;
            this.grantAccessError = null;
            try {
                await window.api.post(
                    `/admin/platform/schools/${this.grantAccessTarget.schoolId}/complimentary-access`,
                    { plan: this.grantAccessForm.plan, durationMonths: this.grantAccessForm.durationMonths });

                this.grantAccessTarget = null;
                await this.load();
            } catch (err) {
                this.grantAccessError = window.api.toMessage(err, "Erreur lors de l'attribution de l'accès.");
            } finally {
                this.isGranting = false;
            }
        }
    }));
});
