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

        // Catégorie affichée (filtre d'affichage côté écran, appliqué aussi au PDF / à l'impression) :
        // '' = les trois listes ; sinon une seule des clés de statusOptions.
        statusFilter: '',
        statusOptions: [
            { value: '', label: 'Toutes les catégories' },
            { value: 'paid', label: 'Ont payé' },
            { value: 'partial', label: 'Ont partiellement payé' },
            { value: 'unpaid', label: "N'ont pas payé" }
        ],

        data: null,
        applied: null,
        pdfBusy: false,
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

        /** Une section (paid | partial | unpaid) est visible si aucun filtre n'est posé ou si c'est la bonne. */
        showSection(key) {
            return !this.statusFilter || this.statusFilter === key;
        },

        /**
         * PDF de la situation affichée (mêmes filtres figés que l'écran + catégorie choisie).
         * `print` = false : téléchargement ; true : ouvre la boîte d'impression du navigateur sur ce PDF.
         */
        async exportPdf(print) {
            if (!this.applied || this.pdfBusy) return;

            this.pdfBusy = true;
            this.error = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }
                const params = this.buildParams(this.applied);
                if (this.statusFilter) params.set('status', this.statusFilter);
                const response = await fetch(`/api/v1/finance/monthly-status/pdf?${params.toString()}`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) {
                    this.error = response.status === 403
                        ? "Vous n'avez plus accès à la situation des mensualités."
                        : 'Erreur lors de la génération du PDF.';
                    return;
                }
                const blob = await response.blob();
                const url = URL.createObjectURL(blob);

                if (print) {
                    // Iframe invisible : on imprime le PDF lui-même, pas la page (menus, filtres, cartes).
                    const frame = document.createElement('iframe');
                    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;';
                    frame.src = url;
                    frame.onload = () => {
                        try {
                            frame.contentWindow.focus();
                            frame.contentWindow.print();
                        } catch (_) {
                            // Aperçu PDF intégré indisponible : repli sur l'ouverture dans un nouvel onglet.
                            window.open(url, '_blank');
                        }
                    };
                    document.body.appendChild(frame);
                    setTimeout(() => { frame.remove(); URL.revokeObjectURL(url); }, 120000);
                } else {
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = this.exportFileName(response, 'pdf');
                    document.body.appendChild(link);
                    link.click();
                    link.remove();
                    URL.revokeObjectURL(url);
                }
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors de la génération du PDF.');
            } finally {
                this.pdfBusy = false;
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

        exportFileName(response, ext = 'xlsx') {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
            if (match) return decodeURIComponent(match[1]);
            return `mensualites_${this.applied ? String(this.applied.month).padStart(2, '0') : ''}.${ext}`;
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
