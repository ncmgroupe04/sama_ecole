/**
 * Module Surveillance / Comptabilité — Pointage Profs (/pointage-profs). Relevé d'heures des
 * enseignants Vacataires (TeacherHourRecord), base de calcul de leur bulletin de paie. Réservé au
 * Directeur et à la Finance (garde réelle : FinanceController, mêmes routes que la modale
 * « Fiche heures — Vacataire » de /paie).
 *
 * window.api.get/post renvoient déjà le JSON désérialisé ou lèvent une erreur normalisée — jamais de
 * response.ok/response.json() ici (même convention que payroll.js/billets.js).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('pointageProfsView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : visionneuse native du navigateur dans la
        // modale _PdfPreviewModal, comme tous les autres écrans qui impriment un document.
        ...window.pdfPreview.state(),

        isLoading: false,
        error: null,

        contracts: [],
        selectedContractId: '',
        filter: { month: new Date().getMonth() + 1, year: new Date().getFullYear() },

        hourRecords: [],
        hourRecordForm: { date: new Date().toISOString().split('T')[0], hours: '', note: '' },
        isSavingHourRecord: false,
        downloadingSheet: false,

        init() {
            this.loadContracts();
        },

        get vacataireContracts() {
            return this.contracts.filter((c) => c.type === 'Vacataire');
        },

        async loadContracts() {
            this.isLoading = true;
            this.error = null;
            try {
                this.contracts = await api.get('/finance/employee-contracts');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des contrats.');
            } finally {
                this.isLoading = false;
            }
        },

        async loadHourRecords() {
            if (!this.selectedContractId) {
                this.hourRecords = [];
                return;
            }
            this.isLoading = true;
            try {
                const params = new URLSearchParams({ month: this.filter.month, year: this.filter.year });
                this.hourRecords = await api.get(`/finance/employee-contracts/${this.selectedContractId}/hour-records?${params.toString()}`);
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des heures.'));
            } finally {
                this.isLoading = false;
            }
        },

        async submitHourRecord() {
            if (!this.selectedContractId || !this.hourRecordForm.date || !this.hourRecordForm.hours) {
                toast.error('Veuillez sélectionner un enseignant, la date et le nombre d\'heures.');
                return;
            }

            this.isSavingHourRecord = true;
            try {
                await api.post(`/finance/employee-contracts/${this.selectedContractId}/hour-records`, {
                    date: this.hourRecordForm.date,
                    hours: Number(this.hourRecordForm.hours),
                    note: this.hourRecordForm.note || null
                });
                toast.success('Heures ajoutées.');
                this.hourRecordForm = { date: new Date().toISOString().split('T')[0], hours: '', note: '' };
                await this.loadHourRecords();
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de l'enregistrement des heures."));
            } finally {
                this.isSavingHourRecord = false;
            }
        },

        /** Fiche d'heures Vacataire : aperçu dans la modale partagée (visionneuse PDF native du navigateur). */
        async downloadHourRecordSheet() {
            if (!this.selectedContractId) return;
            this.downloadingSheet = true;
            try {
                const params = new URLSearchParams({ month: this.filter.month, year: this.filter.year });
                await this.openPdfPreview(
                    `/api/v1/finance/employee-contracts/${this.selectedContractId}/hour-records/sheet/pdf?${params.toString()}`,
                    'Fiche des heures',
                    `Fiche-Heures-${this.selectedContractId}.pdf`);
            } finally {
                this.downloadingSheet = false;
            }
        },

        monthLabel(month) {
            const names = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];
            return names[(month || 1) - 1] || '';
        }
    }));
});
