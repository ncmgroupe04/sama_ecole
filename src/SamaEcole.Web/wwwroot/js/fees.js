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

        // Application d'un montant standard (modale)
        isApplyOpen: false,
        isApplying: false,
        applyForm: { amount: null, overwriteExisting: false },
        applyErrors: {},
        applyReport: null,

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
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des frais.';
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
            this.applyForm = { amount: null, overwriteExisting: false };
            this.applyErrors = {};
            this.applyReport = null;
            this.isApplyOpen = true;
        },

        async submitApplyStandard() {
            this.isApplying = true;
            this.applyErrors = {};
            try {
                this.applyReport = await window.api.post('/finance/fees/apply-standard', {
                    feeCategoryId: this.selectedCategoryId,
                    amount: this.applyForm.amount,
                    overwriteExisting: this.applyForm.overwriteExisting
                });
                await this.reloadFees();
                // La modale reste ouverte pour AFFICHER le compte-rendu ; l'utilisateur la ferme.
            } catch (err) {
                this.applyErrors = window.api.toFieldErrors(err, "Erreur lors de l'application du montant.");
            } finally {
                this.isApplying = false;
            }
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
                this.error = err.message || "Erreur lors du chargement de l'historique.";
                this.historyFor = null;
            } finally {
                this.isLoadingHistory = false;
            }
        },

        closeHistory() {
            this.historyFor = null;
            this.historyEntries = [];
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR') + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        }
    }));
});
