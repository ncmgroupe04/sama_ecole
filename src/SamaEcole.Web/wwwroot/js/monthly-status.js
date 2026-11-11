/**
 * Situation des mensualités (/mensualites). Année scolaire + mois obligatoires, classe facultative, puis
 * « Afficher la situation » : GET /finance/monthly-status → trois listes (payé / partiel / non payé) et un résumé.
 *
 * Le bouton Excel interroge la route jumelle (/finance/monthly-status/excel) avec LES MÊMES filtres que ceux
 * dont la situation affichée est issue (`applied`, figés au clic) : le fichier correspond à l'écran, même si
 * l'utilisateur a touché aux sélecteurs depuis. La garde réelle est la policy CanViewMonthlyStatus côté serveur.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('monthlyStatusView', () => ({
        schoolYears: [],
        classrooms: [],
        schoolYearId: '',
        month: '',
        classroomId: '',

        monthOptions: [
            'Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin',
            'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'
        ].map((label, i) => ({ value: String(i + 1), label })),

        data: null,
        applied: null,
        isLoading: false,
        exporting: false,
        error: null,
        formError: null,

        async init() {
            try {
                const [years, classes] = await Promise.all([
                    window.api.get('/school-years'),
                    window.api.get('/classrooms')
                ]);
                this.schoolYears = years || [];
                this.classrooms = classes || [];
                const active = this.schoolYears.find((y) => y.isActive);
                if (active) this.schoolYearId = active.id;
                this.month = String(new Date().getMonth() + 1);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Impossible de charger les années scolaires et les classes.');
            }
        },

        buildParams(filters) {
            const params = new URLSearchParams({ schoolYearId: filters.schoolYearId, month: String(filters.month) });
            if (filters.classroomId) params.set('classroomId', filters.classroomId);
            return params;
        },

        async load() {
            this.formError = null;
            this.error = null;
            if (!this.schoolYearId || !this.month) {
                this.formError = "Choisissez l'année scolaire et le mois.";
                return;
            }

            const filters = { schoolYearId: this.schoolYearId, month: Number(this.month), classroomId: this.classroomId || '' };
            this.isLoading = true;
            try {
                this.data = await window.api.get(`/finance/monthly-status?${this.buildParams(filters).toString()}`);
                this.applied = filters;
            } catch (err) {
                this.data = null;
                this.applied = null;
                this.error = window.api.toMessage(err, 'Erreur lors du calcul de la situation des mensualités.');
            } finally {
                this.isLoading = false;
            }
        },

        async exportExcel() {
            if (!this.applied || this.exporting) return;

            this.exporting = true;
            this.error = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }
                const response = await fetch(`/api/v1/finance/monthly-status/excel?${this.buildParams(this.applied).toString()}`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) {
                    this.error = response.status === 403
                        ? "Vous n'avez plus accès à la situation des mensualités."
                        : "Erreur lors de l'export Excel.";
                    return;
                }
                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = this.exportFileName(response);
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'export Excel.");
            } finally {
                this.exporting = false;
            }
        },

        exportFileName(response) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
            if (match) return decodeURIComponent(match[1]);
            return `mensualites_${this.applied ? String(this.applied.month).padStart(2, '0') : ''}.xlsx`;
        },

        monthLabel() {
            const m = this.monthOptions.find((o) => o.value === String(this.data ? this.data.month : this.month));
            return m ? m.label : '';
        },

        formatAmount(amount) { return window.formatFCFA(amount); },

        formatDate(value) {
            if (!value) return '—';
            const d = new Date(value);
            return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
        }
    }));
});
