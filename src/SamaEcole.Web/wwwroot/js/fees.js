/**
 * Écran Paramétrage des frais — ticket JGK-F01.
 *
 * Trois ressources composent la grille : les catégories, les classes et le barème (une ligne par
 * catégorie × classe). L'interface les croise pour afficher, catégorie par catégorie, le montant de
 * chaque classe — ou « non défini » là où aucune ligne n'existe encore.
 *
 * Le serveur reste seul juge : le rôle est relu du JWT pour masquer les actions d'écriture, mais
 * l'API répond 403 à qui les appellerait quand même. Le jeton de concurrence (rowVersion/xmin) est
 * renvoyé tel quel à chaque modification — deux éditions concurrentes ne peuvent pas s'écraser
 * (AGENTS.md règle #5).
 *
 * Matrice d'autorisation "Photoshop" : le Directeur peut TOUJOURS modifier/supprimer ; la Finance ne
 * le peut QUE si SchoolSettings.allowFinanceToModifyFees / allowFinanceToDeleteFees est activé pour
 * l'école (canModifyFees / canDeleteFees, deux getters — jamais une valeur figée au chargement, même
 * principe que canManageGradingConfig dans settings.js). Ces deux booléens sont lus une seule fois à
 * l'ouverture de l'écran (comme gradingScale dans grades.js) : un changement de délégation fait par
 * le Directeur pendant que cet écran est déjà ouvert n'est visible qu'au rechargement.
 *
 * Feature D — canModifyFees couvre aussi la CRÉATION d'une catégorie et l'application d'un montant
 * standard (Option 1), pas seulement l'ajustement d'une ligne déjà existante (Option 2) : les trois
 * façonnent le même barème, donc la même délégation (voir FinanceController).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('feesView', () => ({
        categories: [],
        classrooms: [],
        fees: [],
        selectedCategoryId: null,

        activeTab: 'fees', // 'fees' ou 'disbursements'
        disbursements: [],
        isLoadingDisbursements: false,
        
        disbursementForm: { date: '', beneficiary: '', reason: '', category: 'Salaires', amount: null, paymentMethod: 'Cash' },
        creatingDisbursement: false,
        isSubmittingDisbursement: false,
        createDisbursementError: null,

        deletingDisbursement: null,
        isDeletingDisbursement: false,
        deleteDisbursementError: null,

        isLoading: false,
        error: null,

        // Recherche + filtre niveau + tri sur la grille de la catégorie sélectionnée (Volume 5 §6)
        search: '',
        levelFilter: '',
        sortKey: 'name',
        sortDir: 'asc',

        isDirecteur: window.auth.role === 'Directeur',
        allowFinanceToModifyFees: false,
        allowFinanceToDeleteFees: false,

        get canModifyFees() {
            return this.isDirecteur || (window.auth.role === 'Finance' && this.allowFinanceToModifyFees);
        },
        get canDeleteFees() {
            return this.isDirecteur || (window.auth.role === 'Finance' && this.allowFinanceToDeleteFees);
        },

        // Suppression d'une catégorie (modale de confirmation)
        deletingCategory: null, // { id, name }
        isDeletingCategory: false,
        deleteCategoryError: null,
        showCategoryDeletedDialog: false,
        deletedCategoryName: '',

        // Suppression d'une ligne de barème / exception (modale de confirmation)
        deletingFee: null, // { classFeeId, classroomName, rowVersion }
        isDeletingFee: false,
        deleteFeeError: null,
        showFeeDeletedDialog: false,
        deletedFeeClassroomName: '',

        // Création de catégorie (panneau latéral)
        isCategoryOpen: false,
        isSavingCategory: false,
        newCategory: { name: '', isRecurring: true },
        categoryErrors: {},

        // Confirmation « Catégorie ajoutée » affichée après un enregistrement réussi.
        showCategoryAddedDialog: false,
        addedCategoryName: '',

        // Application d'un montant standard (modale). `level` = périmètre : chaîne vide = toutes les
        // classes, sinon le seul niveau ciblé (le serveur ne touche alors pas aux autres cycles).
        isApplyOpen: false,
        isApplying: false,
        applyForm: { amount: null, level: '', overwriteExisting: false },
        applyErrors: {},

        // Édition d'une exception (modale)
        editing: null, // { classFeeId, classroomName, amount, rowVersion }
        isSavingEdit: false,
        editErrors: {},
        showFeeEditedDialog: false,
        editedFeeClassroomName: '',
        editedFeeAmount: null,

        // Historique (modale)
        historyFor: null, // { classroomName }
        historyEntries: [],
        isLoadingHistory: false,

        async init() {
            await this.loadAll();
        },

        async loadAll() {
            this.isLoading = true;
            this.error = null;
            try {
                const [categories, classrooms, fees, settings] = await Promise.all([
                    window.api.get('/finance/fee-categories'),
                    window.api.get('/classrooms'),
                    window.api.get('/finance/fees'),
                    window.api.get('/schools/current/settings')
                ]);
                this.categories = categories;
                this.classrooms = classrooms;
                this.fees = fees;
                this.allowFinanceToModifyFees = settings.allowFinanceToModifyFees;
                this.allowFinanceToDeleteFees = settings.allowFinanceToDeleteFees;

                // Garde la catégorie sélectionnée si elle existe encore, sinon prend la première.
                if (!this.categories.some((c) => c.id === this.selectedCategoryId)) {
                    this.selectedCategoryId = this.categories.length ? this.categories[0].id : null;
                }

                await this.loadDisbursements();
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des frais.');
            } finally {
                this.isLoading = false;
            }
        },

        /** Recharge le seul barème (après une écriture) sans re-solliciter catégories et classes. */
        async reloadFees() {
            this.fees = await window.api.get('/finance/fees');
        },

        get selectedCategory() {
            return this.categories.find((c) => c.id === this.selectedCategoryId) || null;
        },

        /**
         * Segmented control des catégories (Views\Fees\Index.cshtml) : pastille blanche + texte
         * primaire pour l'onglet actif, fond transparent + texte discret (éclairci au survol) pour
         * les autres. Même gabarit que les onglets de /parametres (settings.js tabClass).
         */
        tabClass(categoryId) {
            return this.selectedCategoryId === categoryId
                ? 'bg-white text-indigo-600 font-semibold shadow-sm'
                : 'bg-transparent text-slate-700 font-medium hover:text-slate-900 hover:bg-slate-200/50';
        },

        /**
         * TOUTES les classes croisées avec leur montant pour la catégorie sélectionnée (montant null
         * si aucune ligne n'existe encore). Base non filtrée : sert de dénominateur à definedCount, la
         * recherche/filtre/tri ne s'appliquent qu'à l'affichage (voir rows()).
         */
        get allRows() {
            if (!this.selectedCategoryId) return [];

            return this.classrooms.map((classroom) => {
                const fee = this.fees.find(
                    (f) => f.feeCategoryId === this.selectedCategoryId && f.classroomId === classroom.id);
                return { classroom, fee: fee || null };
            });
        },

        get definedCount() {
            return this.allRows.filter((r) => r.fee).length;
        },

        get knownLevels() {
            return [...new Set(this.classrooms.map((c) => c.level))].sort();
        },

        toggleSort(key) {
            if (this.sortKey === key) {
                this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
            } else {
                this.sortKey = key;
                this.sortDir = 'asc';
            }
        },

        /** Recherche (nom/niveau) + filtre niveau + tri, appliqués à l'affichage sans toucher allRows. */
        get rows() {
            const q = this.search.trim().toLowerCase();
            let rows = this.allRows.filter((r) =>
                (!q || r.classroom.name.toLowerCase().includes(q) || r.classroom.level.toLowerCase().includes(q)) &&
                (!this.levelFilter || r.classroom.level === this.levelFilter));

            const dir = this.sortDir === 'asc' ? 1 : -1;
            return [...rows].sort((a, b) => {
                if (this.sortKey === 'amount') {
                    const va = a.fee ? a.fee.amount : -1;
                    const vb = b.fee ? b.fee.amount : -1;
                    return (va - vb) * dir;
                }
                return String(a.classroom[this.sortKey]).localeCompare(String(b.classroom[this.sortKey])) * dir;
            });
        },

        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return Number(amount).toLocaleString('fr-FR') + ' F';
        },

        // ------------------------------------------------------------ Catégories

        openCreateCategory() {
            this.newCategory = { name: '', isRecurring: true };
            this.categoryErrors = {};
            this.isCategoryOpen = true;
        },

        async submitCreateCategory() {
            this.isSavingCategory = true;
            this.categoryErrors = {};
            try {
                const created = await window.api.post('/finance/fee-categories', this.newCategory);
                this.isCategoryOpen = false;
                this.addedCategoryName = this.newCategory.name;
                await this.loadAll();
                this.selectedCategoryId = created.id; // bascule sur la catégorie qu'on vient de créer
                this.showCategoryAddedDialog = true; // confirmation « Catégorie ajoutée »
            } catch (err) {
                this.categoryErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la catégorie.');
            } finally {
                this.isSavingCategory = false;
            }
        },

        openDeleteCategory(category) {
            this.deletingCategory = category;
            this.deleteCategoryError = null;
        },

        closeDeleteCategory() {
            this.deletingCategory = null;
            this.deleteCategoryError = null;
        },

        /** Supprime la catégorie ET tout son barème en cascade (soft delete côté serveur, AGENTS.md règle #6). */
        async confirmDeleteCategory() {
            if (!this.deletingCategory) return;

            this.isDeletingCategory = true;
            this.deleteCategoryError = null;
            try {
                await window.api.delete(`/finance/fee-categories/${this.deletingCategory.id}`);
                this.deletedCategoryName = this.deletingCategory.name;
                this.deletingCategory = null;
                await this.loadAll();
                this.showCategoryDeletedDialog = true;
            } catch (err) {
                this.deleteCategoryError = (err && err.message) || 'Erreur lors de la suppression de la catégorie.';
            } finally {
                this.isDeletingCategory = false;
            }
        },

        // ------------------------------------------------- Montant standard (Option 1)

        openApplyStandard() {
            // Le périmètre reprend le filtre de niveau actif sur la grille : quand on vient de
            // consulter le Collège, c'est presque toujours le Collège qu'on veut tarifer.
            this.applyForm = { amount: null, level: this.levelFilter, overwriteExisting: false };
            this.applyErrors = {};
            this.isApplyOpen = true;
        },

        /** Classes réellement visées par le formulaire — sert au décompte affiché dans la modale. */
        get applyTargetClassrooms() {
            return this.applyForm.level
                ? this.classrooms.filter((c) => c.level === this.applyForm.level)
                : this.classrooms;
        },

        async submitApplyStandard() {
            this.isApplying = true;
            this.applyErrors = {};
            try {
                const report = await window.api.post('/finance/fees/apply-standard', {
                    feeCategoryId: this.selectedCategoryId,
                    amount: this.applyForm.amount,
                    // Chaîne vide = « toutes les classes » : on envoie null, le contrat d'API ne
                    // connaît que null ou un niveau réel (openapi.yaml, ApplyStandardFeeRequest).
                    level: this.applyForm.level || null,
                    overwriteExisting: this.applyForm.overwriteExisting
                });
                // Succès : on FERME la modale tout de suite et on résume l'action dans un Toast,
                // plutôt qu'un encadré vert qui obligeait l'utilisateur à fermer lui-même.
                this.isApplyOpen = false;
                await this.reloadFees();
                toast.success(this.applyReportMessage(report));
            } catch (err) {
                this.applyErrors = window.api.toFieldErrors(err, "Erreur lors de l'application du montant.");
            } finally {
                this.isApplying = false;
            }
        },

        /** Résumé lisible du compte-rendu d'application ({created, updated, skipped}) pour le Toast. */
        applyReportMessage(report) {
            const applied = (report.created || 0) + (report.updated || 0);
            const parts = [`${applied} montant${applied > 1 ? 's' : ''} appliqué${applied > 1 ? 's' : ''}`];
            if (report.skipped > 0) {
                parts.push(`${report.skipped} inchangé${report.skipped > 1 ? 's' : ''}`);
            }
            return `Frais mis à jour : ${parts.join(', ')}.`;
        },

        // ----------------------------------------------- Exception par classe (Option 2)

        openEdit(row) {
            // Une ligne « non définie » n'a pas d'identifiant : on ne peut pas la modifier seule. Le
            // bouton n'est proposé que sur les lignes existantes (voir la vue) ; garde-fou ici aussi.
            if (!row.fee) return;

            this.editing = {
                classFeeId: row.fee.id,
                classroomName: row.classroom.name,
                amount: row.fee.amount,
                rowVersion: row.fee.rowVersion
            };
            this.editErrors = {};
        },

        closeEdit() {
            this.editing = null;
            this.editErrors = {};
        },

        async submitEdit() {
            if (!this.editing) return;

            this.isSavingEdit = true;
            this.editErrors = {};
            try {
                await window.api.put(`/finance/fees/${this.editing.classFeeId}`, {
                    amount: this.editing.amount,
                    rowVersion: this.editing.rowVersion
                });
                this.editedFeeClassroomName = this.editing.classroomName;
                this.editedFeeAmount = this.editing.amount;
                this.closeEdit();
                await this.reloadFees();
                this.showFeeEditedDialog = true;
            } catch (err) {
                // Conflit optimiste : quelqu'un a modifié cette ligne entre-temps. On recharge pour
                // montrer la valeur réelle et on invite à recommencer, plutôt que d'écraser (règle #5).
                if (err.status === 409) {
                    this.editErrors = { global: 'Ce montant vient d\'être modifié par un autre utilisateur. La grille a été rafraîchie — vérifiez la valeur puis réessayez.' };
                    await this.reloadFees();
                } else {
                    this.editErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEdit = false;
            }
        },

        openDeleteFee(row) {
            // Miroir du garde-fou d'openEdit : une ligne « non définie » n'a pas d'identifiant.
            if (!row.fee) return;

            this.deletingFee = {
                classFeeId: row.fee.id,
                classroomName: row.classroom.name,
                rowVersion: row.fee.rowVersion
            };
            this.deleteFeeError = null;
        },

        closeDeleteFee() {
            this.deletingFee = null;
            this.deleteFeeError = null;
        },

        async confirmDeleteFee() {
            if (!this.deletingFee) return;

            this.isDeletingFee = true;
            this.deleteFeeError = null;
            try {
                await window.api.delete(`/finance/fees/${this.deletingFee.classFeeId}?rowVersion=${this.deletingFee.rowVersion}`);
                this.deletedFeeClassroomName = this.deletingFee.classroomName;
                this.deletingFee = null;
                await this.reloadFees();
                this.showFeeDeletedDialog = true;
            } catch (err) {
                if (err.status === 409) {
                    // Conflit optimiste : quelqu'un a modifié cette ligne entre-temps (règle #5). On
                    // recharge pour montrer la valeur réelle plutôt que de forcer une suppression aveugle.
                    this.deleteFeeError = 'Ce montant vient d\'être modifié par un autre utilisateur. La grille a été rafraîchie — vérifiez la valeur puis réessayez.';
                    await this.reloadFees();
                } else {
                    this.deleteFeeError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingFee = false;
            }
        },

        // ------------------------------------------------------------ Historique

        async openHistory(row) {
            if (!row.fee) return;

            this.historyFor = { classroomName: row.classroom.name };
            this.historyEntries = [];
            this.isLoadingHistory = true;
            try {
                this.historyEntries = await window.api.get(`/finance/fees/${row.fee.id}/history`);
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors du chargement de l'historique.");
                this.historyFor = null;
            } finally {
                this.isLoadingHistory = false;
            }
        },

        closeHistory() {
            this.historyFor = null;
            this.historyEntries = [];
        },

        // ------------------------------------------------------------ Décaissements

        async loadDisbursements() {
            this.isLoadingDisbursements = true;
            try {
                this.disbursements = await window.api.get('/finance/disbursements');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des décaissements.');
            } finally {
                this.isLoadingDisbursements = false;
            }
        },

        openCreateDisbursement() {
            this.createDisbursementError = null;
            this.disbursementForm = {
                date: new Date().toISOString().split('T')[0],
                beneficiary: '',
                reason: '',
                category: 'Salaires',
                amount: null,
                paymentMethod: 'Cash'
            };
            this.creatingDisbursement = true;
        },

        closeCreateDisbursement() {
            this.creatingDisbursement = false;
        },

        async submitCreateDisbursement() {
            this.createDisbursementError = null;
            this.isSubmittingDisbursement = true;
            try {
                await window.api.post('/finance/disbursements', this.disbursementForm);
                this.closeCreateDisbursement();
                await this.loadDisbursements();
            } catch (err) {
                this.createDisbursementError = window.api.toMessage(err, 'Erreur lors de l\'enregistrement.');
            } finally {
                this.isSubmittingDisbursement = false;
            }
        },

        openDeleteDisbursement(disbursement) {
            this.deleteDisbursementError = null;
            this.deletingDisbursement = disbursement;
        },

        closeDeleteDisbursement() {
            this.deletingDisbursement = null;
        },

        async confirmDeleteDisbursement() {
            this.isDeletingDisbursement = true;
            this.deleteDisbursementError = null;
            try {
                await window.api.delete(`/finance/disbursements/${this.deletingDisbursement.id}`);
                this.closeDeleteDisbursement();
                await this.loadDisbursements();
            } catch (err) {
                this.deleteDisbursementError = window.api.toMessage(err, 'Erreur lors de l\'annulation.');
            } finally {
                this.isDeletingDisbursement = false;
            }
        },

        formatDateOnly(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR');
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR') + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        }
    }));
});
