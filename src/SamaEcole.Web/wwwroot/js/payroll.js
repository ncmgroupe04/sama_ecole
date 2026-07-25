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
                this.error = err.message || 'Erreur lors du chargement des contrats.';
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
                toast.error(err.message || "Erreur lors de l'enregistrement du contrat.");
            } finally {
                this.isSavingContract = false;
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
                this.error = err.message || 'Erreur lors du chargement des fiches de paie.';
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
                toast.error(err.message || "Erreur lors de la génération de la fiche de paie.");
            } finally {
                this.isGeneratingPayslip = false;
            }
        },

        /** Bulletin PDF : le jeton ne voyage pas en navigation classique — fetch brut + blob (même mécanique que Billets/Caisse). */
        async printPayslip(fichePaieId) {
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
                toast.error(err.message || 'Erreur lors de la génération du bulletin.');
            } finally {
                this.printingPayslipId = null;
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
                this.error = err.message || 'Erreur lors du chargement des déclarations fiscales.';
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
                toast.error(err.message || 'Erreur lors de la génération de la déclaration.');
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

        /** FCFA : entiers, séparateur de milliers français. Pas de décimales — la monnaie n'en a pas. */
        formatAmount(amount) {
            return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount || 0) + ' FCFA';
        },

        monthLabel(month) {
            const names = ['Janvier', 'Février', 'Mars', 'Avril', 'Mai', 'Juin', 'Juillet', 'Août', 'Septembre', 'Octobre', 'Novembre', 'Décembre'];
            return names[(month || 1) - 1] || '';
        }
    }));
});
