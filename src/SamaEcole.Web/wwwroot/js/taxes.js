/**
 * Module Comptabilité & Fiscalité — Fiscalité (/fiscalite). Cartes KPI de la période sélectionnée
 * (mois + année fiscale), historique de l'année, génération d'une nouvelle déclaration et
 * téléchargement de l'état synthétique en PDF. Réservé au Directeur et à la Finance (garde réelle :
 * FinanceController).
 *
 * La période du sélecteur d'en-tête est la SEULE source de vérité : l'année pilote l'appel API
 * (/finance/tax-declarations?year=…) et le mois sélectionne, côté client, la déclaration affichée
 * dans les cartes — pas de second filtre « Année » au-dessus du tableau, qui pouvait diverger.
 *
 * window.api.get/post renvoient déjà le JSON désérialisé ou lèvent une erreur normalisée — jamais de
 * response.ok/response.json() ici (même convention que payroll.js/discipline.js).
 */
document.addEventListener('alpine:init', () => {
    const MONTH_NAMES = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin',
                         'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];

    Alpine.data('taxesView', () => ({
        // Ouverture PDF partagée (wwwroot/js/pdf-preview.js) : le document s'ouvre dans un nouvel
        // onglet, rendu par la visionneuse PDF native du navigateur. Plus de modale.
        ...window.pdfPreview.state(),

        isLoading: false,
        error: null,

        taxDeclarations: [],

        // Période affichée par les cartes KPI (mois en cours par défaut).
        periodMonth: new Date().getMonth() + 1,
        periodYear: new Date().getFullYear(),

        isTaxModalOpen: false,
        isGeneratingTax: false,
        taxForm: { month: new Date().getMonth() + 1, year: new Date().getFullYear() },

        downloadingPdfId: null,

        init() {
            this.loadTaxDeclarations();
        },

        /** Déclaration de la période choisie, ou null si elle n'a pas encore été générée. */
        get selectedDeclaration() {
            const month = Number(this.periodMonth);
            const year = Number(this.periodYear);
            return this.taxDeclarations.find(d => d.month === month && d.year === year) || null;
        },

        monthOptions() {
            return MONTH_NAMES.map((label, index) => ({ value: index + 1, label }));
        },

        /** Année en cours et les 4 précédentes — au-delà, aucune fiche de paie n'est saisie dans l'app. */
        yearOptions() {
            const current = new Date().getFullYear();
            return Array.from({ length: 5 }, (_, i) => current - i).map(y => ({ value: y, label: String(y) }));
        },

        periodLabel() {
            return `${this.monthLabel(Number(this.periodMonth))} ${this.periodYear}`;
        },

        /** Montant d'un champ de la déclaration courante — 0 FCFA tant qu'aucune n'existe pour la période. */
        amountOf(field) {
            return this.formatAmount(this.selectedDeclaration ? this.selectedDeclaration[field] : 0);
        },

        onPeriodChange() {
            // Le mois ne change que la sélection côté client : la liste de l'année est déjà chargée.
        },

        async onYearChange() {
            await this.loadTaxDeclarations();
        },

        async loadTaxDeclarations() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams();
                if (this.periodYear) params.set('year', this.periodYear);
                this.taxDeclarations = await api.get(`/finance/tax-declarations?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des déclarations fiscales.');
            } finally {
                this.isLoading = false;
            }
        },

        openTaxModal() {
            this.taxForm = { month: new Date().getMonth() + 1, year: new Date().getFullYear() };
            this.isTaxModalOpen = true;
        },

        async submitTaxDeclaration() {
            this.isGeneratingTax = true;
            try {
                await api.post('/finance/tax-declaration', {
                    month: Number(this.taxForm.month),
                    year: Number(this.taxForm.year)
                });
                toast.success('Déclaration fiscale générée.');
                this.isTaxModalOpen = false;
                // On bascule les cartes KPI sur la période qui vient d'être générée, sinon l'écran
                // resterait sur un mois vide alors que l'utilisateur vient de produire la déclaration.
                this.periodMonth = Number(this.taxForm.month);
                this.periodYear = Number(this.taxForm.year);
                await this.loadTaxDeclarations();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la génération de la déclaration.'));
            } finally {
                this.isGeneratingTax = false;
            }
        },

        /** État synthétique : ouverture dans un nouvel onglet (visionneuse PDF native du navigateur). */
        async downloadDeclarationPdf(declarationId) {
            if (!declarationId || declarationId === 'undefined') {
                console.error('Identifiant de déclaration invalide ou indéfini', declarationId);
                return;
            }
            this.downloadingPdfId = declarationId;
            try {
                await this.openPdfPreview(
                    `/api/v1/finance/tax-declarations/${declarationId}/pdf`,
                    'Déclaration fiscale',
                    `Declaration-Fiscale-${declarationId}.pdf`);
            } finally {
                this.downloadingPdfId = null;
            }
        },

        formatAmount(amount) {
            return window.formatFCFA(amount);
        },

        monthLabel(month) {
            const names = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];
            return names[(month || 1) - 1] || '';
        }
    }));
});
