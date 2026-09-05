/**
 * Console Super Admin — Tableau de bord plateforme (Executive Dashboard).
 *
 * Consomme `GET /api/v1/admin/platform/dashboard` (agrégats : établissements, utilisateurs, revenu
 * confirmé, abonnements actifs, MRR, ARR, cashflow prévisionnel 30j, ARPU — vue PostgreSQL
 * `v_platform_dashboard_stats`, migration AddPlatformFinancialKpis) et
 * `GET /api/v1/admin/platform/revenue-projection` (12 points mensuels, GetPlatformRevenueProjectionQuery)
 * pour le graphique de projection : ce dernier reflète les échéances RÉELLES des abonnements Active,
 * PAS une extrapolation — voir la doc de la query pour le détail du calcul. Le Simulateur de Croissance
 * (ARR/MRR projeté pour N écoles cibles) est en revanche un calcul purement client (ARPU × N), aucune
 * hypothèse de croissance n'existe côté serveur.
 *
 * Les deux graphiques « Répartition du MRR par formule » et « Nouveaux établissements/mois » sont
 * dérivés CÔTÉ CLIENT de `GET /admin/platform/subscriptions` et `GET /schools` (déjà utilisés ailleurs
 * dans la console, Billing/Schools) — aucun agrégat serveur dédié n'existe pour ces deux vues, la
 * volumétrie (liste complète, non paginée) rend le calcul en JS négligeable.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminDashboard', () => ({
        data: {},
        projection: [],
        subscriptions: [],
        schools: [],
        isLoading: false,
        isDemoData: false,
        error: null,
        simulatorSchools: 100, // Valeur par défaut pour le simulateur

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                const [dashboard, projection, subscriptions, schools] = await Promise.all([
                    window.api.get('/admin/platform/dashboard'),
                    window.api.get('/admin/platform/revenue-projection'),
                    window.api.get('/admin/platform/subscriptions'),
                    window.api.get('/schools')
                ]);
                this.data = dashboard;
                this.projection = projection;
                this.subscriptions = subscriptions;
                this.schools = schools;
                this.isDemoData = false;
            } catch (err) {
                if (err.status === 404) {
                    this.data = demoDashboardData();
                    this.projection = demoProjectionData();
                    this.isDemoData = true;
                } else {
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement du tableau de bord.');
                }
            } finally {
                this.isLoading = false;
                this.initializeChart();
                this.initializeMrrByPlanChart();
                this.initializeNewSchoolsChart();
            }
        },

        /** MRR par formule (Active uniquement, Yearly ramené à un équivalent mensuel / 12 — même règle que superadmin-billing.js::formatPeriodicValue). */
        get mrrByPlan() {
            const totals = { Primaire: 0, Standard: 0, Premium: 0 };
            for (const sub of this.subscriptions) {
                if (sub.status !== 'Active' || !sub.lastPaymentAmountXof || !(sub.plan in totals)) continue;
                totals[sub.plan] += sub.lastPaymentBillingPeriod === 'Yearly'
                    ? sub.lastPaymentAmountXof / 12
                    : sub.lastPaymentAmountXof;
            }
            return totals;
        },

        /** Nouveaux établissements par mois de création, 12 derniers mois (mois courant inclus, en dernier). */
        get newSchoolsByMonth() {
            const now = new Date();
            const months = Array.from({ length: 12 }, (_, i) => {
                const d = new Date(now.getFullYear(), now.getMonth() - (11 - i), 1);
                return { key: `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`, label: d.toLocaleDateString('fr-FR', { month: 'short' }), count: 0 };
            });
            const byKey = Object.fromEntries(months.map((m) => [m.key, m]));

            for (const school of this.schools) {
                const d = new Date(school.createdAt);
                const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
                if (byKey[key]) byKey[key].count++;
            }
            return months;
        },

        get kpiTiles() {
            const d = this.data;
            return [
                {
                    label: 'MRR', icon: 'wallet',
                    iconBg: 'bg-sky-500/15', iconText: 'text-sky-400',
                    value: this.formatCompactXof(d.mrr ?? 0)
                },
                {
                    label: 'ARR', icon: 'chart-multiple',
                    iconBg: 'bg-violet-500/15', iconText: 'text-violet-400',
                    value: this.formatCompactXof(d.arr ?? 0)
                },
                {
                    label: 'Prévisionnel (30j)', icon: 'wallet',
                    iconBg: 'bg-emerald-500/15', iconText: 'text-emerald-400',
                    value: this.formatCompactXof(d.forecastedRevenue30Days ?? 0)
                },
                {
                    label: 'ARPU', icon: 'users',
                    iconBg: 'bg-amber-500/15', iconText: 'text-amber-400',
                    value: this.formatCompactXof(d.arpu ?? 0)
                },
                {
                    label: 'Établissements', icon: 'building',
                    iconBg: 'bg-sky-500/15', iconText: 'text-sky-400',
                    value: this.formatCompactNumber(d.totalSchools ?? 0)
                },
                {
                    label: 'Utilisateurs', icon: 'users',
                    iconBg: 'bg-violet-500/15', iconText: 'text-violet-400',
                    value: this.formatCompactNumber(d.totalUsers ?? 0)
                },
                {
                    label: 'Revenu confirmé', icon: 'wallet',
                    iconBg: 'bg-emerald-500/15', iconText: 'text-emerald-400',
                    value: this.formatCompactXof(d.totalRevenue ?? 0)
                },
                {
                    label: 'Abonnements actifs', icon: 'chart-multiple',
                    iconBg: 'bg-amber-500/15', iconText: 'text-amber-400',
                    value: this.formatCompactNumber(d.activeSubscriptions ?? 0)
                }
            ];
        },

        initializeChart() {
            if (!this.$refs.canvas || typeof Chart === 'undefined') return;

            const ctx = this.$refs.canvas.getContext('2d');
            if (this.chart) {
                this.chart.destroy();
            }

            // Données RÉELLES (échéances des abonnements Active) — voir GetPlatformRevenueProjectionQuery.
            const points = this.projection || [];
            const labels = points.map((p) => this.formatMonthLabel(p.month));
            const projectedRevenue = points.map((p) => p.projectedRevenue ?? 0);

            this.chart = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [{
                        label: 'Revenu projeté',
                        data: projectedRevenue,
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
                            grid: { color: 'rgba(255, 255, 255, 0.05)' },
                            ticks: { 
                                color: '#9ca3af',
                                callback: (value) => new Intl.NumberFormat('fr-FR', { notation: 'compact' }).format(value)
                            }
                        },
                        x: {
                            grid: { display: false },
                            ticks: { color: '#9ca3af' }
                        }
                    }
                }
            });
        },

        /** Histogramme empilé (une seule colonne « MRR », un segment par formule) — composition du MRR total. */
        initializeMrrByPlanChart() {
            if (!this.$refs.mrrByPlanCanvas || typeof Chart === 'undefined') return;

            const ctx = this.$refs.mrrByPlanCanvas.getContext('2d');
            if (this.mrrByPlanChart) this.mrrByPlanChart.destroy();

            const totals = this.mrrByPlan;
            const plans = [
                { key: 'Primaire', color: '#f59e0b' },
                { key: 'Standard', color: '#38bdf8' },
                { key: 'Premium', color: '#a78bfa' }
            ];

            this.mrrByPlanChart = new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: ['MRR'],
                    datasets: plans.map((p) => ({
                        label: p.key,
                        data: [totals[p.key] || 0],
                        backgroundColor: p.color,
                        borderRadius: 4
                    }))
                },
                options: {
                    indexAxis: 'y',
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { position: 'bottom', labels: { color: '#9ca3af', boxWidth: 12, padding: 16 } }
                    },
                    scales: {
                        x: {
                            stacked: true,
                            beginAtZero: true,
                            grid: { color: 'rgba(255, 255, 255, 0.05)' },
                            ticks: {
                                color: '#9ca3af',
                                callback: (value) => new Intl.NumberFormat('fr-FR', { notation: 'compact' }).format(value)
                            }
                        },
                        y: {
                            stacked: true,
                            grid: { display: false },
                            ticks: { color: '#9ca3af' }
                        }
                    }
                }
            });
        },

        /** Nouveaux établissements par mois, 12 derniers mois. */
        initializeNewSchoolsChart() {
            if (!this.$refs.newSchoolsCanvas || typeof Chart === 'undefined') return;

            const ctx = this.$refs.newSchoolsCanvas.getContext('2d');
            if (this.newSchoolsChart) this.newSchoolsChart.destroy();

            const months = this.newSchoolsByMonth;

            this.newSchoolsChart = new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: months.map((m) => m.label),
                    datasets: [{
                        label: 'Nouveaux établissements',
                        data: months.map((m) => m.count),
                        backgroundColor: '#34d399',
                        borderRadius: 4
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

        formatCompactXof(amount) {
            return new Intl.NumberFormat('fr-FR', { notation: 'compact', style: 'currency', currency: 'XOF', maximumFractionDigits: 1 }).format(amount || 0);
        },

        formatCompactNumber(n) {
            return new Intl.NumberFormat('fr-FR', { notation: 'compact', maximumFractionDigits: 1 }).format(n || 0);
        },

        /** `month` au format "yyyy-MM" (MonthlyRevenueProjectionDto) → libellé court localisé (ex. "sept."). */
        formatMonthLabel(month) {
            const [year, m] = (month || '').split('-').map(Number);
            if (!year || !m) return '';
            return new Date(year, m - 1, 1).toLocaleDateString('fr-FR', { month: 'short' });
        }
    }));
});

/** Jeu de données de démonstration — affiché UNIQUEMENT quand le bandeau amber l'annonce (voir load()). */
function demoDashboardData() {
    return {
        totalSchools: 27,
        totalUsers: 184,
        totalRevenue: 4820000,
        activeSubscriptions: 24,
        mrr: 450000,
        arr: 5400000,
        forecastedRevenue30Days: 450000,
        arpu: 22500
    };
}

/** Projection de démonstration (12 mois) — même bandeau que demoDashboardData(). */
function demoProjectionData() {
    const points = [];
    const now = new Date();
    for (let i = 0; i < 12; i++) {
        const d = new Date(now.getFullYear(), now.getMonth() + i, 1);
        const month = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
        // Base mensuelle + un pic ponctuel pour illustrer un lot de renouvellements Yearly.
        const projectedRevenue = 380000 + (i % 4 === 0 ? 150000 : 0);
        points.push({ month, projectedRevenue });
    }
    return points;
}
