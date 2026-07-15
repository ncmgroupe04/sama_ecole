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
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('feesView', () => ({
        categories: [],
        classrooms: [],
        fees: [],
        selectedCategoryId: null,

        isLoading: false,
        error: null,

        isDirecteur: window.auth.role === 'Directeur',

        // Création de catégorie (panneau latéral)
        isCategoryOpen: false,
        isSavingCategory: false,
        newCategory: { name: '', isRecurring: true },
        categoryErrors: {},

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
                const [categories, classrooms, fees] = await Promise.all([
                    window.api.get('/finance/fee-categories'),
                    window.api.get('/classrooms'),
                    window.api.get('/finance/fees')
                ]);
                this.categories = categories;
                this.classrooms = classrooms;
                this.fees = fees;

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
         * Les lignes de la grille pour la catégorie sélectionnée : TOUTES les classes, chacune avec
         * son montant s'il existe, ou null (« non défini ») sinon. C'est ce croisement qui montre d'un
         * coup d'œil ce qu'il reste à paramétrer.
         */
        get rows() {
            if (!this.selectedCategoryId) return [];

            return this.classrooms.map((classroom) => {
                const fee = this.fees.find(
                    (f) => f.feeCategoryId === this.selectedCategoryId && f.classroomId === classroom.id);
                return { classroom, fee: fee || null };
            });
        },

        get definedCount() {
            return this.rows.filter((r) => r.fee).length;
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
                await this.loadAll();
                this.selectedCategoryId = created.id; // bascule sur la catégorie qu'on vient de créer
            } catch (err) {
                this.categoryErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la catégorie.');
            } finally {
                this.isSavingCategory = false;
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
                this.closeEdit();
                await this.reloadFees();
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
