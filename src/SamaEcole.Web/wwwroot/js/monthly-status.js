/**
 * Situation des mensualités (/mensualites). Année scolaire + mois obligatoires, classe facultative :
 * GET /finance/monthly-status → trois listes (payé / partiel / non payé) et un résumé.
 *
 * Design fluide 2026 : la situation se charge dès l'arrivée (année active, mois courant) et à chaque
 * changement de mois, d'année ou de classe — plus de bouton « Afficher ». Le RUBAN (un segment par
 * catégorie, largeur proportionnelle au nombre d'élèves) sert de filtre : un clic sur un segment ne
 * montre que ces élèves, et le PDF / l'impression suivent ce filtre.
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
                if (this.schoolYearId) await this.load();
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

        // ------------------------------------------------------------------ Bande de mois

        /**
         * Les douze mois dans l'ORDRE DE L'ANNÉE SCOLAIRE choisie (son mois de début en premier, d'après
         * startDate), en libellés courts. Sans année connue : septembre en premier.
         */
        get schoolMonths() {
            const short = ['Janv.', 'Févr.', 'Mars', 'Avr.', 'Mai', 'Juin', 'Juil.', 'Août', 'Sept.', 'Oct.', 'Nov.', 'Déc.'];
            const year = this.schoolYears.find((y) => y.id === this.schoolYearId);
            const parsed = year && year.startDate ? Number(String(year.startDate).slice(5, 7)) : 9;
            const start = parsed >= 1 && parsed <= 12 ? parsed : 9;
            return Array.from({ length: 12 }, (_, i) => {
                const value = ((start - 1 + i) % 12) + 1;
                return { value: String(value), label: short[value - 1], full: this.monthOptions[value - 1].label };
            });
        },

        selectMonth(value) {
            if (this.isLoading || String(this.month) === String(value)) return;
            this.month = String(value);
            this.load();
        },

        // ------------------------------------------------------------------ Ruban et liste

        /** Les trois segments du ruban, dans l'ordre d'affichage. `count` pilote la largeur. */
        get segments() {
            if (!this.data) return [];
            return [
                { key: 'paid', label: 'Ont payé', count: this.data.paid.length },
                { key: 'partial', label: 'Partiellement', count: this.data.partial.length },
                { key: 'unpaid', label: "N'ont rien payé", count: this.data.unpaid.length }
            ];
        },

        /** Clic sur un segment : filtre sur cette catégorie ; second clic : retour à tous les élèves. */
        toggleStatus(key) {
            this.statusFilter = this.statusFilter === key ? '' : key;
        },

        /**
         * Largeur d'un segment : proportionnelle à son effectif (plancher 1 pour qu'un segment à zéro
         * reste cliquable et lisible) ; le segment filtré s'élargit pour marquer la sélection.
         */
        segmentStyle(segment) {
            const grow = Math.max(segment.count, 1) * (this.statusFilter === segment.key ? 1.8 : 1);
            return { flexGrow: String(grow) };
        },

        /**
         * Une seule liste : les impayés d'abord (ce que le comptable doit traiter), puis les partiels,
         * puis ceux qui ont payé — réduite à la catégorie du ruban quand un filtre est posé.
         */
        get rows() {
            if (!this.data) return [];
            const tag = (list, kind) => list.map((r) => ({ ...r, kind }));
            return [
                ...tag(this.data.unpaid, 'unpaid'),
                ...tag(this.data.partial, 'partial'),
                ...tag(this.data.paid, 'paid')
            ].filter((r) => !this.statusFilter || r.kind === this.statusFilter);
        },

        listTitle() {
            const found = this.statusOptions.find((o) => o.value === this.statusFilter);
            return this.statusFilter && found ? found.label : 'Tous les élèves';
        },

        /** Part du mois déjà réglée, de 0 à 100 (0 si rien n'est attendu). */
        paidPercent(row) {
            if (!row.expectedAmount || row.expectedAmount <= 0) return 0;
            return Math.min(100, Math.max(0, Math.round((row.paidAmount / row.expectedAmount) * 100)));
        },

        recoveryRate() {
            const s = this.data ? this.data.summary : null;
            if (!s || !s.totalExpected) return null;
            return Math.round((s.totalCollected / s.totalExpected) * 100);
        },

        /** Initiales de la pastille : première lettre des deux premiers mots du nom. */
        initials(name) {
            return String(name || '').trim().split(/\s+/).slice(0, 2).map((part) => part.charAt(0).toUpperCase()).join('');
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
