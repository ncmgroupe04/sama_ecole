/**
 * Console Super Admin — Tableau de bord plateforme (refonte UI/UX).
 *
 * TODO BACKEND : `GET /api/v1/admin/platform/dashboard` n'existe pas encore. Aucune query
 * d'agrégation multi-écoles (nombre d'élèves cumulé, MRR, volume de notes) n'existe côté
 * SamaEcole.Application — GetSchoolsQuery ne renvoie que les métadonnées d'établissement (voir
 * src/SamaEcole.Application/Schools/Queries/GetSchools). Il faudra une nouvelle Query MediatR
 * (ex. GetPlatformDashboardQuery) qui agrège School/Subscription/SubscriptionPayment — ces deux
 * dernières tables sont protégées par RLS PostgreSQL par tenant (AGENTS.md règle #2) et le Super
 * Admin n'a AUCUN schoolId dans son JWT : une requête EF Core normale via IApplicationDbContext ne
 * lui renverra RIEN sur ces tables, RLS oblige. Le futur Handler devra donc soit interroger via le
 * rôle propriétaire `sama_ecole` (jamais utilisé par l'app aujourd'hui, réservé aux migrations —
 * décision à documenter et à faire valider explicitement, pas un contournement silencieux), soit
 * agréger via une vue/fonction PostgreSQL `SECURITY DEFINER` dédiée et auditée. À concevoir comme
 * son propre ticket de sécurité avant implémentation.
 *
 * En attendant, cet écran affiche un jeu de données de démonstration (bandeau explicite) dès que
 * l'appel échoue en 404 — jamais silencieusement, voir load().
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminDashboard', () => ({
        data: {},
        mrrSeries: [],
        isLoading: false,
        isDemoData: false,
        error: null,
        showMrrTable: false,
        hoverIndex: null,

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.data = await window.api.get('/admin/platform/dashboard');
                this.mrrSeries = this.data.mrrSeries || [];
                this.isDemoData = false;
            } catch (err) {
                if (err.status === 404) {
                    this.data = demoDashboardData();
                    this.mrrSeries = this.data.mrrSeries;
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
                    label: 'Écoles actives', icon: 'building',
                    iconBg: 'bg-sky-500/15', iconText: 'text-sky-400',
                    value: d.activeSchoolsCount ?? '—',
                    deltaLabel: `${d.suspendedSchoolsCount ?? 0} suspendue(s)`, deltaClass: 'text-zinc-500'
                },
                {
                    label: 'MRR', icon: 'wallet',
                    iconBg: 'bg-emerald-500/15', iconText: 'text-emerald-400',
                    value: this.formatCompactXof(d.mrrXof ?? 0),
                    deltaLabel: this.deltaLabel(d.mrrDeltaPct), deltaClass: this.deltaClass(d.mrrDeltaPct)
                },
                {
                    label: 'Élèves cumulés', icon: 'users',
                    iconBg: 'bg-violet-500/15', iconText: 'text-violet-400',
                    value: this.formatCompactNumber(d.totalStudents ?? 0),
                    deltaLabel: 'Toutes écoles confondues', deltaClass: 'text-zinc-500'
                },
                {
                    label: 'Notes saisies', icon: 'chart-multiple',
                    iconBg: 'bg-amber-500/15', iconText: 'text-amber-400',
                    value: this.formatCompactNumber(d.totalGradesEntered ?? 0),
                    deltaLabel: 'Cumul plateforme', deltaClass: 'text-zinc-500'
                }
            ];
        },

        get healthMeters() {
            const h = this.data.serverHealth || {};
            const uptime = h.uptimePct ?? 0;
            const apiMs = h.apiLatencyMs ?? 0;
            const dbMs = h.dbLatencyMs ?? 0;

            return [
                {
                    label: 'Disponibilité API (30j)', icon: 'server',
                    pct: Math.min(uptime, 100), valueLabel: `${uptime.toFixed(2)} %`,
                    ...this.severityClasses(this.severity(uptime, 99.5, 98, true))
                },
                {
                    label: 'Latence API', icon: 'globe',
                    pct: Math.min((apiMs / 500) * 100, 100), valueLabel: `${Math.round(apiMs)} ms`,
                    ...this.severityClasses(this.severity(apiMs, 200, 500, false))
                },
                {
                    label: 'Latence PostgreSQL', icon: 'database',
                    pct: Math.min((dbMs / 200) * 100, 100), valueLabel: `${Math.round(dbMs)} ms`,
                    ...this.severityClasses(this.severity(dbMs, 80, 200, false))
                }
            ];
        },

        /** `invert=true` : plus haut est meilleur (ex. disponibilité %). Sinon plus bas est meilleur (latence). */
        severity(value, warnAt, critAt, invert) {
            if (invert) {
                if (value >= warnAt) return 'good';
                return value >= critAt ? 'warn' : 'crit';
            }
            if (value <= warnAt) return 'good';
            return value <= critAt ? 'warn' : 'crit';
        },

        severityClasses(sev) {
            return {
                good: { textClass: 'text-emerald-400', trackClass: 'bg-emerald-500/15', fillClass: 'bg-emerald-400' },
                warn: { textClass: 'text-amber-400', trackClass: 'bg-amber-500/15', fillClass: 'bg-amber-400' },
                crit: { textClass: 'text-rose-400', trackClass: 'bg-rose-500/15', fillClass: 'bg-rose-400' }
            }[sev];
        },

        // ------------------------------------------------------------ Graphique MRR (SVG, viewBox 560x180)

        mrrPoints() {
            const series = this.mrrSeries;
            if (!series.length) return [];

            const values = series.map((s) => s.amountXof);
            const max = Math.max(...values);
            const min = Math.min(...values);
            const range = max - min || 1;
            const yTop = 20, yBottom = 160;

            return series.map((s, i) => ({
                x: series.length > 1 ? (i / (series.length - 1)) * 560 : 280,
                y: yBottom - ((s.amountXof - min) / range) * (yBottom - yTop)
            }));
        },

        mrrLinePath() {
            const pts = this.mrrPoints();
            if (!pts.length) return '';
            return pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(' ');
        },

        mrrAreaPath() {
            const pts = this.mrrPoints();
            if (!pts.length) return '';
            const line = this.mrrLinePath();
            return `${line} L${pts[pts.length - 1].x.toFixed(1)} 160 L${pts[0].x.toFixed(1)} 160 Z`;
        },

        // ------------------------------------------------------------ Formatage

        deltaLabel(pct) {
            if (pct === undefined || pct === null) return '—';
            return `${pct > 0 ? '+' : ''}${pct.toFixed(1)} % vs mois dernier`;
        },

        deltaClass(pct) {
            if (!pct) return 'text-zinc-500';
            return pct > 0 ? 'text-emerald-400' : 'text-rose-400';
        },

        formatXof(amount) {
            return new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'XOF', maximumFractionDigits: 0 }).format(amount || 0);
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
    const months = [];
    const now = new Date();
    let base = 1450000;

    for (let i = 11; i >= 0; i--) {
        const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
        base = Math.round(base * (1 + (0.02 + Math.random() * 0.05)));
        months.push({
            monthLabel: d.toLocaleDateString('fr-FR', { month: 'short', year: 'numeric' }),
            amountXof: base
        });
    }

    const mrrXof = months[months.length - 1].amountXof;
    const mrrPrev = months[months.length - 2].amountXof;

    return {
        activeSchoolsCount: 27,
        suspendedSchoolsCount: 2,
        mrrXof,
        mrrDeltaPct: ((mrrXof - mrrPrev) / mrrPrev) * 100,
        totalStudents: 9840,
        totalGradesEntered: 214300,
        serverHealth: { apiLatencyMs: 118, dbLatencyMs: 34, uptimePct: 99.97 },
        mrrSeries: months
    };
}
