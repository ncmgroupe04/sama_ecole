/**
 * Console Super Admin — Établissements (refonte UI/UX). Consomme le vrai GET /api/v1/schools
 * (SchoolsController, ticket JGK-B01, [Authorize(Roles = SuperAdmin)]) : liste complète, filtrage et
 * recherche instantanée côté client (la liste des écoles reste de taille modeste — pas besoin de
 * pagination serveur pour l'instant, contrairement au journal d'audit).
 *
 * Bouton « Infiltrer » : POST /admin/platform/schools/{schoolId}/impersonate
 * (ImpersonateSchoolCommand) renvoie un jeton d'impersonation à courte durée de vie portant
 * l'identité du Directeur de l'école ciblée. auth.enterImpersonation() bascule la session dessus et
 * on redirige vers le tableau de bord tenant — _Layout.cshtml affiche alors le bandeau permettant d'en
 * sortir (wwwroot/js/auth.js, impersonationBanner).
 *
 * Les deux graphiques (statuts, courbe d'inscriptions) sont dérivés CÔTÉ CLIENT de cette même liste —
 * même raisonnement que superadmin-dashboard.js/superadmin-billing.js.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminSchools', () => ({
        schools: [],
        isLoading: false,
        error: null,
        searchQuery: '',
        statusFilter: 'All',

        impersonateTarget: null, // { id, name }
        isImpersonating: false,
        impersonateError: null,

        statusTarget: null, // { id, name, currentStatus, newStatus }
        statusReason: '',
        isChangingStatus: false,
        statusError: null,

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.schools = await window.api.get('/schools');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des établissements.');
            } finally {
                this.isLoading = false;
                this.initializeStatusChart();
                this.initializeGrowthChart();
            }
        },

        /**
         * Courbe CUMULATIVE d'inscriptions : total d'établissements atteint mois après mois, sur les 12
         * derniers mois — la forme classique d'une courbe de croissance (contrairement à l'histogramme
         * « Nouveaux établissements/mois » du tableau de bord, qui compte les ajouts PAR mois, pas le
         * cumul). `count` inclut tout établissement créé AVANT ou PENDANT le mois, y compris ceux
         * créés avant la fenêtre des 12 mois — la courbe part donc du total déjà acquis, pas de zéro.
         */
        get cumulativeGrowth() {
            const now = new Date();
            const months = Array.from({ length: 12 }, (_, i) => {
                const d = new Date(now.getFullYear(), now.getMonth() - (11 - i), 1);
                return { end: new Date(d.getFullYear(), d.getMonth() + 1, 1), label: d.toLocaleDateString('fr-FR', { month: 'short' }), count: 0 };
            });

            for (const school of this.schools) {
                const createdAt = new Date(school.createdAt);
                for (const month of months) {
                    if (createdAt < month.end) month.count++;
                }
            }
            return months;
        },

        initializeStatusChart() {
            if (!this.$refs.statusCanvas || typeof Chart === 'undefined') return;

            const ctx = this.$refs.statusCanvas.getContext('2d');
            if (this.statusChart) this.statusChart.destroy();

            this.statusChart = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: ['Actifs', 'Suspendus', 'Bloqués'],
                    datasets: [{
                        data: [this.countByStatus('Active'), this.countByStatus('Suspended'), this.countByStatus('Blocked')],
                        backgroundColor: ['#34d399', '#fbbf24', '#fb7185'],
                        borderColor: '#18181b',
                        borderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { position: 'bottom', labels: { color: '#9ca3af', boxWidth: 12, padding: 16 } }
                    }
                }
            });
        },

        initializeGrowthChart() {
            if (!this.$refs.growthCanvas || typeof Chart === 'undefined') return;

            const ctx = this.$refs.growthCanvas.getContext('2d');
            if (this.growthChart) this.growthChart.destroy();

            const months = this.cumulativeGrowth;

            this.growthChart = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: months.map((m) => m.label),
                    datasets: [{
                        label: 'Établissements (cumul)',
                        data: months.map((m) => m.count),
                        borderColor: '#38bdf8',
                        backgroundColor: 'rgba(56, 189, 248, 0.1)',
                        borderWidth: 2,
                        fill: true,
                        tension: 0.4
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { display: false }
                    },
                    scales: {
                        y: {
                            beginAtZero: true,
                            ticks: { color: '#9ca3af', precision: 0 },
                            grid: { color: 'rgba(255, 255, 255, 0.05)' }
                        },
                        x: {
                            grid: { display: false },
                            ticks: { color: '#9ca3af' }
                        }
                    }
                }
            });
        },

        get filteredSchools() {
            const query = this.searchQuery.trim().toLowerCase();

            return this.schools.filter((school) => {
                const matchesStatus = this.statusFilter === 'All' || school.status === this.statusFilter;
                const matchesQuery = !query || [school.name, school.address, school.phone]
                    .filter(Boolean)
                    .some((field) => field.toLowerCase().includes(query));

                return matchesStatus && matchesQuery;
            });
        },

        countByStatus(status) {
            return this.schools.filter((s) => s.status === status).length;
        },

        statusLabel(status) {
            return { Active: 'Actif', Suspended: 'Suspendu', Blocked: 'Bloqué' }[status] || status;
        },

        statusClasses(status) {
            return {
                Active: 'bg-emerald-500/15 text-emerald-400 ring-1 ring-inset ring-emerald-500/20',
                Suspended: 'bg-amber-500/15 text-amber-400 ring-1 ring-inset ring-amber-500/20',
                Blocked: 'bg-rose-500/15 text-rose-400 ring-1 ring-inset ring-rose-500/20'
            }[status] || 'bg-zinc-800 text-zinc-300';
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleDateString('fr-FR');
        },

        // ------------------------------------------------------------ Infiltrer

        openImpersonateConfirm(school) {
            this.impersonateTarget = { id: school.id, name: school.name };
            this.impersonateError = null;
        },

        closeImpersonateConfirm() {
            this.impersonateTarget = null;
        },

        async confirmImpersonate() {
            if (!this.impersonateTarget) return;

            this.isImpersonating = true;
            this.impersonateError = null;
            try {
                const tokens = await window.api.post(`/admin/platform/schools/${this.impersonateTarget.id}/impersonate`);
                window.auth.enterImpersonation(tokens);
                window.location.assign('/tableau-de-bord');
            } catch (err) {
                this.impersonateError = window.api.toMessage(err, "Erreur lors de l'infiltration.");
                this.isImpersonating = false;
            }
        },

        // ------------------------------------------------------------ Changement de statut

        openStatusConfirm(school, newStatus) {
            this.statusTarget = { id: school.id, name: school.name, newStatus };
            this.statusReason = '';
            this.statusError = null;
        },

        closeStatusConfirm() {
            this.statusTarget = null;
        },

        statusModalTitle() {
            const labels = { Active: 'Réactiver l\'établissement', Suspended: 'Suspendre l\'établissement', Blocked: 'Bloquer l\'établissement' };
            return this.statusTarget ? (labels[this.statusTarget.newStatus] || 'Changer le statut') : '';
        },

        async confirmStatusChange() {
            if (!this.statusTarget) return;

            if (!this.statusReason.trim()) {
                this.statusError = 'Le motif est obligatoire.';
                return;
            }

            this.isChangingStatus = true;
            this.statusError = null;
            try {
                await window.api.patch(`/schools/${this.statusTarget.id}/status`, {
                    status: this.statusTarget.newStatus,
                    reason: this.statusReason.trim()
                });
                this.closeStatusConfirm();
                await this.load();
            } catch (err) {
                this.statusError = window.api.toMessage(err, 'Erreur lors du changement de statut.');
            } finally {
                this.isChangingStatus = false;
            }
        }
    }));
});
