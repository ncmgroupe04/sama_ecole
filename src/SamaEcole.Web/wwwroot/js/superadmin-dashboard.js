/**
 * Console Super Admin — Tableau de bord plateforme.
 *
 * Consomme le vrai `GET /api/v1/admin/platform/dashboard` (PlatformController, migration
 * AddPlatformAdminViews : vue PostgreSQL `v_platform_dashboard_stats`, SECURITY DEFINER-like via
 * security_invoker = false). Volontairement limité aux QUATRE agrégats que cette vue expose
 * (établissements, utilisateurs, revenu confirmé, abonnements actifs) — le MRR sur 12 mois, les
 * effectifs élèves cumulés, les notes saisies et la santé serveur nécessiteraient chacun leur propre
 * agrégation (et pour la santé serveur, un monitoring dédié) : hors périmètre de cette vue, à
 * concevoir dans un futur ticket plutôt que d'inventer des chiffres ici.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminDashboard', () => ({
        data: {},
        isLoading: false,
        isDemoData: false,
        error: null,

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.data = await window.api.get('/admin/platform/dashboard');
                this.isDemoData = false;
            } catch (err) {
                if (err.status === 404) {
                    this.data = demoDashboardData();
                    this.isDemoData = true;
                } else {
                    this.error = err.message || 'Erreur lors du chargement du tableau de bord.';
                }
            } finally {
                this.isLoading = false;
            }
        },

        get kpiTiles() {
            const d = this.data;
            return [
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

        formatCompactXof(amount) {
            return new Intl.NumberFormat('fr-FR', { notation: 'compact', style: 'currency', currency: 'XOF', maximumFractionDigits: 1 }).format(amount || 0);
        },

        formatCompactNumber(n) {
            return new Intl.NumberFormat('fr-FR', { notation: 'compact', maximumFractionDigits: 1 }).format(n || 0);
        }
    }));
});

/** Jeu de données de démonstration — affiché UNIQUEMENT quand le bandeau amber l'annonce (voir load()). */
function demoDashboardData() {
    return {
        totalSchools: 27,
        totalUsers: 184,
        totalRevenue: 4820000,
        activeSubscriptions: 24
    };
}
