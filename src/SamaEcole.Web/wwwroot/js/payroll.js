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
                const data = await api.get('/teachers?page=1&pageSize=1000');
                this.teachers = data.items || [];
            } catch (err) {
                console.error('Erreur chargement enseignants:', err);
            }
        },

        /** GET /users est réservé au Directeur (UsersController) : la Finance ne pourra créer que des contrats Enseignant. */
        async loadUsers() {
            try {
                this.users = await api.get('/users');
            } catch (err) {
                this.users = [];
            }
        },

        openContractModal() {
            this.contractForm = {
                employeeKind: 'teacher', teacherId: '', userId: '',
                type: 'Permanent', baseSalary: '', hourlyRate: '', transportAllowance: ''
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
                    transportAllowance: Number(this.contractForm.transportAllowance) || 0
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
            this.isPayslipModalOpen = true;
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

        /** Bulletin PDF : le jeton ne voyage pas en navigation classique — fetch brut + blob (même mécanique que Billets/Caisse). */
        async printPayslip(fichePaieId) {
            if (!fichePaieId || fichePaieId === 'undefined') {
                console.error('Identifiant de fiche de paie invalide ou indéfini', fichePaieId);
                return;
            }
            this.printingPayslipId = fichePaieId;
            try {
                const response = await fetch(`/api/v1/finance/payroll/${fichePaieId}/pdf`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` }
                });
                if (!response.ok) {
                    throw new Error(`Le serveur a renvoyé ${response.status}.`);
                }
                const blob = new Blob([await response.blob()], { type: 'application/pdf' });
                const url = URL.createObjectURL(blob);
                const win = window.open(url, '_blank');
                if (!win) {
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `Bulletin-${fichePaieId}.pdf`;
                    link.click();
                }
                setTimeout(() => URL.revokeObjectURL(url), 60000);
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la génération du bulletin.'));
            } finally {
                this.printingPayslipId = null;
            }
        },

        /** Attestation de travail PDF — même mécanique que printPayslip (fetch brut + blob, nouvel onglet). */
        async downloadWorkCertificate(contract) {
            if (!contract || !contract.id || contract.id === 'undefined') {
                console.error('Identifiant de contrat invalide ou indéfini', contract && contract.id);
                return;
            }
            this.downloadingCertificateId = contract.id;
            try {
                const response = await fetch(`/api/v1/finance/employee-contracts/${contract.id}/work-certificate/pdf`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` }
                });
                if (!response.ok) {
                    throw new Error(`Le serveur a renvoyé ${response.status}.`);
                }
                const blob = new Blob([await response.blob()], { type: 'application/pdf' });
                const url = URL.createObjectURL(blob);
                const win = window.open(url, '_blank');
                if (!win) {
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `Attestation-Travail-${contract.employeeFullName}.pdf`;
                    link.click();
                }
                setTimeout(() => URL.revokeObjectURL(url), 60000);
            } catch (err) {
                toast.error(window.api.toMessage(err, "Erreur lors de la génération de l'attestation."));
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

        /** Fiche heures PDF du mois/année sélectionné — même mécanique que printPayslip. */
        async previewHourRecordSheet() {
            if (!this.hourRecordsContract) return;
            this.downloadingHourRecordSheet = true;
            try {
                const params = new URLSearchParams({ month: this.hourRecordsFilter.month, year: this.hourRecordsFilter.year });
                const response = await fetch(
                    `/api/v1/finance/employee-contracts/${this.hourRecordsContract.id}/hour-records/sheet/pdf?${params.toString()}`,
                    { headers: { Authorization: `Bearer ${window.auth.accessToken}` } });
                if (!response.ok) {
                    throw new Error(`Le serveur a renvoyé ${response.status}.`);
                }
                const blob = new Blob([await response.blob()], { type: 'application/pdf' });
                const url = URL.createObjectURL(blob);
                const win = window.open(url, '_blank');
                if (!win) {
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `Fiche-Heures-${this.hourRecordsContract.employeeFullName}.pdf`;
                    link.click();
                }
                setTimeout(() => URL.revokeObjectURL(url), 60000);
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors de la génération de la fiche.'));
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
