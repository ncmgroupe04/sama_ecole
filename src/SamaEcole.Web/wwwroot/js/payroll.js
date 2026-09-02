/**
 * Module Comptabilité & Fiscalité — Paie (/paie). Trois onglets : Contrats (prérequis à toute fiche
 * de paie), Fiches de paie (génération + bulletin PDF), Déclarations fiscales (récapitulatif mensuel
 * des charges sociales). Réservé au Directeur et à la Finance (garde réelle : FinanceController).
 *
 * window.api.get/post renvoient déjà le JSON désérialisé ou lèvent une erreur normalisée — jamais de
 * response.ok/response.json() ici (voir le correctif appliqué à discipline.js).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('payrollView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : bulletin de paie, attestation de travail et
        // fiche d'heures s'ouvrent dans la modale _PdfPreviewModal (impression / téléchargement au
        // choix), jamais un download forcé.
        ...window.pdfPreview.state(),

        tab: 'contrats',
        isLoading: false,
        error: null,

        // ---------------------------------------------------------------- Contrats
        contracts: [],
        teachers: [],
        users: [],
        isContractModalOpen: false,
        isSavingContract: false,
        contractForm: {
            employeeKind: 'teacher',
            teacherId: '',
            userId: '',
            type: 'Permanent',
            baseSalary: '',
            hourlyRate: '',
            transportAllowance: ''
        },

        // Modification (Volume 1 §14.1) : augmentation de salaire ou révision du taux horaire sur un contrat ACTIF.
        isEditContractModalOpen: false,
        isSavingEditContract: false,
        editContractForm: { id: '', type: '', baseSalary: '', hourlyRate: '', transportAllowance: '', reason: '', rowVersion: 0 },

        // Clôture (Volume 1 §14.1) : jamais une suppression — le contrat reste consultable, mais ne génère plus de fiche de paie.
        isCloseContractModalOpen: false,
        isClosingContract: false,
        closeContractForm: { id: '', employeeFullName: '', endDate: new Date().toISOString().split('T')[0], reason: '', rowVersion: 0 },

        // ---------------------------------------------------------------- Fiches de paie
        payslips: [],
        filterMonth: '',
        filterYear: new Date().getFullYear(),
        isPayslipModalOpen: false,
        isGeneratingPayslip: false,
        payslipForm: {
            employeeContractId: '',
            month: new Date().getMonth() + 1,
            year: new Date().getFullYear(),
            hoursWorked: 0,
            transportAllowance: 0
        },
        printingPayslipId: null,
        downloadingCertificateId: null,

        // Suggestion d'heures (ticket JGK-K01) — voir loadSuggestedHours ci-dessous.
        suggestedHours: null,
        isLoadingSuggestion: false,
        suggestionError: null,

        // ---------------------------------------------------------------- Fiche heures Vacataire
        isHourRecordsModalOpen: false,
        hourRecordsContract: null,
        hourRecordsFilter: { month: new Date().getMonth() + 1, year: new Date().getFullYear() },
        hourRecords: [],
        hourRecordForm: { date: new Date().toISOString().split('T')[0], hours: '', note: '' },
        isSavingHourRecord: false,
        downloadingHourRecordSheet: false,

        // ---------------------------------------------------------------- Déclarations fiscales
        taxDeclarations: [],
        filterTaxYear: new Date().getFullYear(),
        isTaxModalOpen: false,
        isGeneratingTax: false,
        taxForm: { month: new Date().getMonth() + 1, year: new Date().getFullYear() },

        init() {
            this.loadContracts();
            this.loadTeachers();
            this.loadUsers();
            this.loadPayslips();
            this.loadTaxDeclarations();
        },

        tabClass(name) {
            return this.tab === name
                ? 'bg-white text-indigo-600 font-semibold shadow-sm'
                : 'bg-transparent text-slate-700 font-medium hover:text-slate-900 hover:bg-slate-200/50';
        },

        // ---------------------------------------------------------------- Contrats

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

        async loadTeachers() {
            try {
                // pageSize plafonné à 100 côté serveur (GetTeachersQueryValidator.MaxPageSize) : au-delà,
                // la requête est rejetée en 422 et le sélecteur reste vide sans qu'aucune erreur ne
                // s'affiche à l'écran (bug réel constaté le 27/08/2026 — pageSize=1000 échouait toujours).
                // 100 reste une limite pour un établissement à très gros effectif enseignant ; passer par
                // une recherche serveur comme students.js le fait serait la vraie solution si ça arrive.
                const data = await api.get('/teachers?page=1&pageSize=100');
                this.teachers = data.items || [];
            } catch (err) {
                console.error('Erreur chargement enseignants:', err);
                this.teachers = [];
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des enseignants.'));
            }
        },

        /** GET /users est réservé au Directeur (UsersController) : la Finance ne pourra créer que des contrats Enseignant. */
        async loadUsers() {
            try {
                this.users = await api.get('/users');
            } catch (err) {
                // silence-volontaire: GET /users est réservé au Directeur (UsersController). Un 403 est
                // le cas NORMAL pour la Finance, qui ne crée alors que des contrats Enseignant — c'est
                // exactement ce que dit le commentaire de la méthode. Un toast transformerait ce refus
                // attendu en incident à chaque chargement de l'écran Paie.
                this.users = [];
            }
        },

        // Moyens de paiement RH (ticket JGK-K02) — distincts du PaymentMethod du module Finance élèves.
        payoutMethodOptions: [
            { value: 'Cash', label: 'Espèces' },
            { value: 'BankTransfer', label: 'Virement bancaire' },
            { value: 'Wave', label: 'Wave' },
            { value: 'OrangeMoney', label: 'Orange Money' }
        ],

        payoutMethodLabel(method) {
            const found = this.payoutMethodOptions.find((o) => o.value === method);
            return found ? found.label : (method || 'Espèces');
        },

        openContractModal() {
            this.contractForm = {
                employeeKind: 'teacher', teacherId: '', userId: '',
                type: 'Permanent', baseSalary: '', hourlyRate: '', transportAllowance: '',
                payoutMethod: 'Cash', payoutAccountReference: ''
            };
            this.isContractModalOpen = true;
        },

        async submitContract() {
            const employeeId = this.contractForm.employeeKind === 'teacher' ? this.contractForm.teacherId : this.contractForm.userId;
            if (!employeeId) {
                toast.error('Veuillez sélectionner un employé.');
                return;
            }

            this.isSavingContract = true;
            try {
                await api.post('/finance/employee-contracts', {
                    teacherId: this.contractForm.employeeKind === 'teacher' ? this.contractForm.teacherId : null,
                    userId: this.contractForm.employeeKind === 'user' ? this.contractForm.userId : null,
                    type: this.contractForm.type,
                    baseSalary: Number(this.contractForm.baseSalary) || 0,
                    hourlyRate: Number(this.contractForm.hourlyRate) || 0,
                    transportAllowance: Number(this.contractForm.transportAllowance) || 0,
                    payoutMethod: this.contractForm.payoutMethod,
                    payoutAccountReference: this.contractForm.payoutAccountReference || null
                });
                toast.success('Contrat enregistré.');
                this.isContractModalOpen = false;
                await this.loadContracts();
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de l'enregistrement du contrat."));
            } finally {
                this.isSavingContract = false;
            }
        },

        // Modification d'un contrat actif (Volume 1 §14.1).
        openEditContractModal(contract) {
            this.editContractForm = {
                id: contract.id,
                type: contract.type,
                baseSalary: contract.baseSalary,
                hourlyRate: contract.hourlyRate,
                transportAllowance: contract.transportAllowance,
                payoutMethod: contract.payoutMethod || 'Cash',
                payoutAccountReference: contract.payoutAccountReference || '',
                reason: '',
                rowVersion: contract.rowVersion
            };
            this.isEditContractModalOpen = true;
        },

        async submitEditContract() {
            if (!this.editContractForm.reason || this.editContractForm.reason.trim().length < 5) {
                toast.error('Le motif est obligatoire (5 caractères minimum).');
                return;
            }

            this.isSavingEditContract = true;
            try {
                await api.patch(`/finance/employee-contracts/${this.editContractForm.id}`, {
                    baseSalary: Number(this.editContractForm.baseSalary) || 0,
                    hourlyRate: Number(this.editContractForm.hourlyRate) || 0,
                    transportAllowance: Number(this.editContractForm.transportAllowance) || 0,
                    payoutMethod: this.editContractForm.payoutMethod,
                    payoutAccountReference: this.editContractForm.payoutAccountReference || null,
                    reason: this.editContractForm.reason.trim(),
                    rowVersion: this.editContractForm.rowVersion
                });
                toast.success('Contrat modifié.');
                this.isEditContractModalOpen = false;
                await this.loadContracts();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la modification du contrat.'));
            } finally {
                this.isSavingEditContract = false;
            }
        },

        // Clôture définitive d'un contrat (Volume 1 §14.1) — jamais une suppression.
        openCloseContractModal(contract) {
            this.closeContractForm = {
                id: contract.id,
                employeeFullName: contract.employeeFullName,
                endDate: new Date().toISOString().split('T')[0],
                reason: '',
                rowVersion: contract.rowVersion
            };
            this.isCloseContractModalOpen = true;
        },

        async submitCloseContract() {
            if (!this.closeContractForm.reason || this.closeContractForm.reason.trim().length < 5) {
                toast.error('Le motif est obligatoire (5 caractères minimum).');
                return;
            }

            this.isClosingContract = true;
            try {
                await api.post(`/finance/employee-contracts/${this.closeContractForm.id}/close`, {
                    endDate: this.closeContractForm.endDate,
                    reason: this.closeContractForm.reason.trim(),
                    rowVersion: this.closeContractForm.rowVersion
                });
                toast.success('Contrat clôturé.');
                this.isCloseContractModalOpen = false;
                await this.loadContracts();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la clôture du contrat.'));
            } finally {
                this.isClosingContract = false;
            }
        },

        // ---------------------------------------------------------------- Fiches de paie

        async loadPayslips() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams();
                if (this.filterMonth) params.set('month', this.filterMonth);
                if (this.filterYear) params.set('year', this.filterYear);
                this.payslips = await api.get(`/finance/payroll?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des fiches de paie.');
            } finally {
                this.isLoading = false;
            }
        },

        applyPayslipFilters() {
            this.loadPayslips();
        },

        openPayslipModal() {
            this.payslipForm = {
                employeeContractId: '', month: new Date().getMonth() + 1, year: new Date().getFullYear(),
                hoursWorked: 0, transportAllowance: 0
            };
            this.suggestedHours = null;
            this.suggestionError = null;
            this.isPayslipModalOpen = true;
        },

        /** Contrat sélectionné dans le formulaire de génération, pour connaître son type (Vacataire ou non). */
        get payslipSelectedContract() {
            return this.contracts.find((c) => c.id === this.payslipForm.employeeContractId) || null;
        },

        get payslipContractIsVacataire() {
            return this.payslipSelectedContract && this.payslipSelectedContract.type === 'Vacataire';
        },

        /**
         * Suggestion d'heures pour la paie du vacataire (ticket JGK-K01) — PUREMENT CONSULTATIVE :
         * agrège les heures pointées (TeacherHourRecord) du mois choisi et les rapproche de l'emploi
         * du temps planifié. Ne pré-remplit rien tant que la Direction n'a pas explicitement cliqué
         * « Utiliser cette suggestion » — un écart signalé ici n'empêche jamais la génération de la fiche.
         */
        async loadSuggestedHours() {
            if (!this.payslipForm.employeeContractId || !this.payslipContractIsVacataire) return;

            this.isLoadingSuggestion = true;
            this.suggestionError = null;
            this.suggestedHours = null;
            try {
                const params = new URLSearchParams({ month: this.payslipForm.month, year: this.payslipForm.year });
                this.suggestedHours = await api.get(
                    `/finance/employee-contracts/${this.payslipForm.employeeContractId}/suggested-hours?${params.toString()}`);
            } catch (err) {
                this.suggestionError = window.api.toMessage(err, 'Erreur lors du calcul de la suggestion.');
            } finally {
                this.isLoadingSuggestion = false;
            }
        },

        /** Reprend la suggestion dans le champ « Heures travaillées » — la Direction reste seule à valider. */
        applySuggestedHours() {
            if (!this.suggestedHours) return;
            this.payslipForm.hoursWorked = this.suggestedHours.suggestedHours;
        },

        /** Une suggestion affichée pour un autre contrat n'a plus de sens dès que la sélection change. */
        onPayslipContractChanged() {
            this.suggestedHours = null;
            this.suggestionError = null;
        },

        async submitPayslip() {
            if (!this.payslipForm.employeeContractId) {
                toast.error('Veuillez sélectionner un contrat.');
                return;
            }

            this.isGeneratingPayslip = true;
            try {
                await api.post('/finance/payroll', {
                    employeeContractId: this.payslipForm.employeeContractId,
                    month: Number(this.payslipForm.month),
                    year: Number(this.payslipForm.year),
                    hoursWorked: Number(this.payslipForm.hoursWorked) || 0,
                    transportAllowance: Number(this.payslipForm.transportAllowance) || 0
                });
                toast.success('Fiche de paie générée.');
                this.isPayslipModalOpen = false;
                await this.loadPayslips();
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de la génération de la fiche de paie."));
            } finally {
                this.isGeneratingPayslip = false;
            }
        },

        /** Bulletin PDF ouvert dans la modale d'aperçu partagée (pdf-preview.js) : impression / téléchargement au choix. */
        async printPayslip(fichePaieId) {
            if (!fichePaieId || fichePaieId === 'undefined') {
                console.error('Identifiant de fiche de paie invalide ou indéfini', fichePaieId);
                return;
            }
            this.printingPayslipId = fichePaieId;
            try {
                await this.openPdfPreview(
                    `/api/v1/finance/payroll/${fichePaieId}/pdf`,
                    'Bulletin de paie',
                    `Bulletin-${fichePaieId}.pdf`
                );
            } finally {
                this.printingPayslipId = null;
            }
        },

        /** Attestation de travail PDF — ouverte dans la modale d'aperçu partagée (même mécanique que printPayslip). */
        async downloadWorkCertificate(contract) {
            if (!contract || !contract.id || contract.id === 'undefined') {
                console.error('Identifiant de contrat invalide ou indéfini', contract && contract.id);
                return;
            }
            this.downloadingCertificateId = contract.id;
            try {
                await this.openPdfPreview(
                    `/api/v1/finance/employee-contracts/${contract.id}/work-certificate/pdf`,
                    'Attestation de travail',
                    `Attestation-Travail-${contract.employeeFullName}.pdf`
                );
            } finally {
                this.downloadingCertificateId = null;
            }
        },

        // ---------------------------------------------------------------- Fiche heures Vacataire

        openHourRecordsModal(contract) {
            this.hourRecordsContract = contract;
            this.hourRecordsFilter = { month: new Date().getMonth() + 1, year: new Date().getFullYear() };
            this.hourRecordForm = { date: new Date().toISOString().split('T')[0], hours: '', note: '' };
            this.isHourRecordsModalOpen = true;
            this.loadHourRecords();
        },

        async loadHourRecords() {
            if (!this.hourRecordsContract) return;
            try {
                const params = new URLSearchParams({ month: this.hourRecordsFilter.month, year: this.hourRecordsFilter.year });
                this.hourRecords = await api.get(`/finance/employee-contracts/${this.hourRecordsContract.id}/hour-records?${params.toString()}`);
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des heures.'));
            }
        },

        async submitHourRecord() {
            if (!this.hourRecordsContract || !this.hourRecordForm.date || !this.hourRecordForm.hours) {
                toast.error('Veuillez renseigner la date et le nombre d\'heures.');
                return;
            }

            this.isSavingHourRecord = true;
            try {
                await api.post(`/finance/employee-contracts/${this.hourRecordsContract.id}/hour-records`, {
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

        /** Fiche heures PDF du mois/année sélectionné — ouverte dans la modale d'aperçu partagée. */
        async previewHourRecordSheet() {
            if (!this.hourRecordsContract) return;
            this.downloadingHourRecordSheet = true;
            try {
                const params = new URLSearchParams({ month: this.hourRecordsFilter.month, year: this.hourRecordsFilter.year });
                await this.openPdfPreview(
                    `/api/v1/finance/employee-contracts/${this.hourRecordsContract.id}/hour-records/sheet/pdf?${params.toString()}`,
                    "Fiche d'heures — Vacataire",
                    `Fiche-Heures-${this.hourRecordsContract.employeeFullName}.pdf`
                );
            } finally {
                this.downloadingHourRecordSheet = false;
            }
        },

        // ---------------------------------------------------------------- Déclarations fiscales

        async loadTaxDeclarations() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams();
                if (this.filterTaxYear) params.set('year', this.filterTaxYear);
                this.taxDeclarations = await api.get(`/finance/tax-declarations?${params.toString()}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des déclarations fiscales.');
            } finally {
                this.isLoading = false;
            }
        },

        applyTaxFilters() {
            this.loadTaxDeclarations();
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
                await this.loadTaxDeclarations();
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la génération de la déclaration.'));
            } finally {
                this.isGeneratingTax = false;
            }
        },

        // ---------------------------------------------------------------- Affichage

        employeeLabel(kind) {
            return kind === 'teacher' ? 'Enseignant' : 'Utilisateur (non-enseignant)';
        },

        contractTypeLabel(type) {
            return type === 'Vacataire' ? 'Vacataire (horaire)' : 'Permanent';
        },

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatAmount(amount) {
            return window.formatFCFA(amount);
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleDateString('fr-FR');
        },

        monthLabel(month) {
            const names = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];
            return names[(month || 1) - 1] || '';
        }
    }));
});
