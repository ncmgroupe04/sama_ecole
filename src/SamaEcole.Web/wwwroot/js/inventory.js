/**
 * Module Inventaire (/inventaire) — patrimoine, journal de stock et prêts de matériel, commun aux
 * écoles publiques (tables-bancs, manuels d'État) et privées (parc informatique, tenues).
 *
 * Un « bien » côté API est un LOT (voir InventoryItem), pas une unité physique : le suivi à l'unité
 * se fait avec un lot de quantité 1. Cet écran ne mélange jamais quantité et code-barres par unité.
 *
 * Point central à ne jamais contourner ici : `quantityAvailable` n'est JAMAIS envoyé par ce fichier
 * dans une requête de modification de fiche (PUT /inventory/items/{id}) — il ne varie que par un
 * mouvement de stock (POST /inventory/movements) ou une fiche de prêt/retour, sous le jeton `rowVersion`
 * du BIEN, jamais celui de la fiche affichée (voir la doc de chaque commande côté API).
 */
document.addEventListener('alpine:init', () => {
    const CONDITION_LABELS = {
        Neuf: 'Neuf',
        Bon: 'Bon état',
        AReparer: 'À réparer',
        HorsService: 'Hors service'
    };

    const CONDITION_OPTIONS = [
        { value: 'Neuf', label: 'Neuf' },
        { value: 'Bon', label: 'Bon état' },
        { value: 'AReparer', label: 'À réparer' },
        { value: 'HorsService', label: 'Hors service' }
    ];

    // Les 4 mouvements qu'un utilisateur peut SAISIR (Attribution/Restitution/PerteSurPret sont
    // produits exclusivement par les endpoints de prêt — voir RecordStockMovementCommand côté API).
    const MOVEMENT_REQUEST_TYPE_OPTIONS = [
        { value: 'Entree', label: 'Entrée' },
        { value: 'Sortie', label: 'Sortie' },
        { value: 'Ajustement', label: "Ajustement d'inventaire" },
        { value: 'MiseAuRebut', label: 'Mise au rebut' }
    ];

    // Tout ce que le JOURNAL peut afficher, y compris les mouvements produits par les prêts.
    const MOVEMENT_TYPE_LABELS = {
        Entree: 'Entrée',
        Sortie: 'Sortie',
        AjustementPositif: 'Ajustement (+)',
        AjustementNegatif: 'Ajustement (−)',
        Attribution: 'Attribution',
        Restitution: 'Restitution',
        MiseAuRebut: 'Mise au rebut',
        PerteSurPret: 'Perte sur prêt'
    };

    const BENEFICIARY_TYPE_LABELS = {
        Eleve: 'Élève',
        Enseignant: 'Enseignant(e)',
        Personnel: 'Personnel'
    };

    const BENEFICIARY_TYPE_OPTIONS = [
        { value: 'Eleve', label: 'Élève' },
        { value: 'Enseignant', label: 'Enseignant(e)' },
        { value: 'Personnel', label: 'Personnel administratif' }
    ];

    const ASSIGNMENT_STATUS_LABELS = {
        EnCours: 'En cours',
        Restitue: 'Restitué',
        PartiellementRestitue: 'Partiellement restitué',
        Perdu: 'Perdu'
    };

    const ASSIGNMENT_STATUS_FILTER_OPTIONS = [
        { value: '', label: 'Tous les statuts' },
        { value: 'EnCours', label: 'En cours' },
        { value: 'PartiellementRestitue', label: 'Partiellement restitué' },
        { value: 'Restitue', label: 'Restitué' },
        { value: 'Perdu', label: 'Perdu' }
    ];

    Alpine.data('inventoryView', () => ({
        ...window.pdfPreview.state(),

        tab: 'catalogue', // 'catalogue' | 'categories' | 'mouvements' | 'prets'
        error: null,

        // Catalogue/mouvements : réservé au Directeur et au Secrétariat côté serveur
        // (InventoryController.CatalogRoles). Confort d'affichage — la protection réelle est l'API.
        canManageCatalog: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // Mouvements de stock et prêts : ouvert en plus au Surveillant, qui distribue les manuels à
        // la rentrée et les récupère en juin (InventoryController.StockRoles).
        canManageStock: ['Directeur', 'Secretariat', 'Surveillant'].includes(window.auth.role),

        // ---------------------------------------------------------------- Données de référence
        categories: [],
        rooms: [],

        // Liste complète des biens pour les listes déroulantes des modales (mouvement/prêt) — jamais
        // pour l'affichage du catalogue, qui reste paginé via `items`. Rechargée à CHAQUE ouverture
        // d'une de ces modales : c'est ce qui garde le jeton rowVersion de chaque option raisonnablement
        // frais (voir onMovementItemSelected/onAssignmentItemSelected).
        pickerItems: [],

        beneficiariesLoaded: false,
        students: [],
        teachers: [],
        users: [],

        get categoryOptions() {
            return this.categories.map((c) => ({ value: c.id, label: c.name }));
        },

        /** KPI de l'en-tête : agrégats déjà calculés par GetInventoryCategoriesQuery, jamais resommés depuis la page paginée du Catalogue. */
        get totalItemsCount() {
            return this.categories.reduce((sum, c) => sum + (c.itemCount || 0), 0);
        },

        get totalUnitsCount() {
            return this.categories.reduce((sum, c) => sum + (c.totalQuantity || 0), 0);
        },

        get roomOptions() {
            return this.rooms.map((r) => ({ value: r.id, label: `${r.name} (${r.buildingName})` }));
        },

        // Un consommable ne se prête jamais (voir CreateItemAssignmentCommandHandler) : la liste
        // déroulante d'un prêt ne les propose donc même pas, plutôt que de laisser l'utilisateur
        // découvrir le refus en 422 après avoir rempli tout le formulaire.
        get loanablePickerItems() {
            return this.pickerItems.filter((i) => !i.isConsumable);
        },

        conditionOptions: CONDITION_OPTIONS,
        conditionFilterOptions: [{ value: '', label: 'Tous états' }].concat(CONDITION_OPTIONS),

        // On n'offre un type de bénéficiaire que si sa liste a pu être chargée : le Surveillant n'a
        // accès ni à /users (Directeur seul) ni à /teachers (Directeur/Secrétariat), et proposer
        // « Personnel » ou « Enseignant » avec un menu vide ne ferait que rejouer le 403 au submit.
        // Il lui reste « Élève », le bénéficiaire réel d'un prêt de manuels.
        get beneficiaryTypeOptions() {
            return BENEFICIARY_TYPE_OPTIONS.filter((o) => {
                if (o.value === 'Personnel') return this.users.length > 0;
                if (o.value === 'Enseignant') return this.teachers.length > 0;
                return true;
            });
        },

        conditionLabel(value) { return CONDITION_LABELS[value] || value; },
        movementTypeLabel(value) { return MOVEMENT_TYPE_LABELS[value] || value; },
        beneficiaryTypeLabel(value) { return BENEFICIARY_TYPE_LABELS[value] || value; },
        assignmentStatusLabel(value) { return ASSIGNMENT_STATUS_LABELS[value] || value; },

        /** DateOnly (« 2026-09-15 ») → jj/mm/aaaa, sans passer par un Date() qui déraperait d'un jour selon le fuseau. */
        formatDate(isoDate) {
            if (!isoDate) return '—';
            const [year, month, day] = isoDate.split('-');
            return `${day}/${month}/${year}`;
        },

        formatAmount(amount) {
            return amount === null || amount === undefined ? '—' : window.formatFCFA(amount);
        },

        /**
         * `''` → `null`, sinon `Number(value)`. Un `decimal?` C# refuse de désérialiser une chaîne
         * vide (System.Text.Json), qui partirait sinon en 400 brut au lieu d'une validation lisible —
         * même piège qu'un `Guid?` reçu à blanc. Toujours utilisé au SUBMIT, jamais sur x-model
         * directement (un champ numérique reste une simple chaîne pendant la saisie).
         */
        toNullableNumber(value) {
            return value === '' || value === null || value === undefined ? null : Number(value);
        },

        async init() {
            await this.loadReferenceData();
            await this.loadItems();
        },

        // Barre d'onglets partagée (.tab-nav-scroll / .tab-btn / .tab-btn-active, Styles/input.css) :
        // ne renvoie que le modificateur actif, la base `.tab-btn` est posée dans la vue.
        tabClass(name) {
            return this.tab === name ? 'tab-btn-active' : '';
        },

        switchTab(tab) {
            this.tab = tab;
            if (tab === 'categories' && this.categories.length === 0) this.loadCategories();
            if (tab === 'mouvements') {
                this.loadMovements();
                // Alimente le filtre « Bien » du journal — sans ce chargement, la liste déroulante
                // resterait vide tant qu'aucune modale de mouvement/prêt n'a été ouverte.
                if (this.pickerItems.length === 0) this.loadPickerItems();
            }
            if (tab === 'prets') this.loadAssignments();
        },

        /** Catégories et salles : nécessaires dès l'onglet Catalogue (filtres, création d'un bien). */
        async loadReferenceData() {
            try {
                const [categories, buildings] = await Promise.all([
                    window.api.get('/inventory/categories'),
                    window.api.get('/buildings')
                ]);
                this.categories = categories || [];
                this.rooms = (buildings || []).flatMap((b) =>
                    b.rooms.map((r) => ({ id: r.id, name: r.name, buildingName: b.name })));
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des données de référence.');
            }
        },

        /**
         * Récupère la TOTALITÉ d'une liste paginée, une page de `pageSize` à la fois. Toute route
         * paginée du projet refuse un `pageSize` supérieur à 100 (GetStudentsQueryValidator et ses
         * pairs — « sans ce plafond, le client dicte la taille de la réponse ») : demander une seule
         * page surdimensionnée (1000, par exemple) ne renvoie donc pas une grande page, elle est
         * refusée en 422. Les sélecteurs de ce module ont besoin de la liste ENTIÈRE (un élève ou un
         * bien absent du menu déroulant serait tout simplement injoignable) — d'où la boucle, plutôt
         * qu'un plafond silencieux sur la première page.
         */
        async fetchAllPages(endpoint) {
            const pageSize = 100;
            let page = 1;
            let items = [];
            let totalCount = Infinity;

            while (items.length < totalCount) {
                const separator = endpoint.includes('?') ? '&' : '?';
                const data = await window.api.get(`${endpoint}${separator}page=${page}&pageSize=${pageSize}`);
                items = items.concat((data && data.items) || []);
                totalCount = (data && data.totalCount) || 0;
                page += 1;
            }

            return items;
        },

        async loadPickerItems() {
            try {
                this.pickerItems = await this.fetchAllPages('/inventory/items');
            } catch (err) {
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des biens.'));
            }
        },

        /**
         * Élèves, enseignants et personnel pour le sélecteur de bénéficiaire — chargés une seule fois.
         *
         * Les trois listes se chargent INDÉPENDAMMENT, chacune tolérante à un 403 : selon le rôle,
         * l'API en refuse certaines sans qu'un prêt en devienne impossible.
         *   • /students — ouvert à tout rôle authentifié (StudentsController) ;
         *   • /teachers — réservé Directeur/Secrétariat/Super Admin (TeachersController.ViewRoles) :
         *     403 pour le Surveillant, qui distribue pourtant les manuels à la rentrée. La matrice
         *     (Volume 7 §21) l'autorise à prêter, mais pas à consulter le corps professoral — il
         *     prête aux ÉLÈVES. On avale donc ce 403 attendu ;
         *   • /users — réservé au Directeur (UsersController), même traitement.
         * Regrouper /students et /teachers dans un même Promise.all transformait ce 403 attendu en
         * « Erreur HTTP 403 » à l'ouverture de « Nouveau prêt » pour la Surveillance.
         */
        async loadBeneficiaries() {
            if (this.beneficiariesLoaded) return;

            let studentsError = null;
            const [students, teachers, users] = await Promise.all([
                this.fetchAllPages('/students').catch((err) => { studentsError = err; return []; }),
                this.fetchAllPages('/teachers').catch(() => []),
                window.api.get('/users').catch(() => [])
            ]);

            this.students = students;
            this.teachers = teachers;
            this.users = users || [];

            // Seul l'échec du chargement des ÉLÈVES est signalé : sans eux, aucun prêt courant n'est
            // possible. Un 403 sur enseignants ou personnel laisse simplement ces options vides.
            if (studentsError) {
                toast.error(window.api.toMessage(studentsError, 'Erreur lors du chargement des bénéficiaires possibles.'));
                return;
            }

            this.beneficiariesLoaded = true;
        },

        get beneficiaryOptionsForType() {
            const type = this.assignmentForm.beneficiaryType;
            if (type === 'Eleve') {
                return this.students.map((s) => ({ value: s.id, label: `${s.fullName} (${s.matricule})` }));
            }
            if (type === 'Enseignant') {
                return this.teachers.map((t) => ({ value: t.id, label: `${t.fullName} (${t.matricule})` }));
            }
            if (type === 'Personnel') {
                return this.users.map((u) => ({ value: u.id, label: u.fullName }));
            }
            return [];
        },

        // ================================================================== ONGLET CATALOGUE
        items: [],
        itemsTotalCount: 0,
        itemsPage: 1,
        itemsPageSize: 20,
        isLoadingItems: false,
        itemFilters: { categoryId: '', roomId: '', condition: '', search: '', outOfStockOnly: false },

        async loadItems() {
            this.isLoadingItems = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.itemsPage, pageSize: this.itemsPageSize });
                if (this.itemFilters.categoryId) params.set('categoryId', this.itemFilters.categoryId);
                if (this.itemFilters.roomId) params.set('roomId', this.itemFilters.roomId);
                if (this.itemFilters.condition) params.set('condition', this.itemFilters.condition);
                if (this.itemFilters.search) params.set('search', this.itemFilters.search);
                if (this.itemFilters.outOfStockOnly) params.set('outOfStockOnly', 'true');

                const data = await window.api.get(`/inventory/items?${params.toString()}`);
                this.items = (data && data.items) || [];
                this.itemsTotalCount = (data && data.totalCount) || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du catalogue.');
            } finally {
                this.isLoadingItems = false;
            }
        },

        applyItemFilters() {
            this.itemsPage = 1;
            this.loadItems();
        },

        resetItemFilters() {
            this.itemFilters = { categoryId: '', roomId: '', condition: '', search: '', outOfStockOnly: false };
            this.applyItemFilters();
        },

        // ------------------------------------------------------------ Biens : création
        isCreateItemOpen: false,
        isSavingItem: false,
        newItem: {
            name: '', code: '', categoryId: '', initialQuantity: 0, condition: 'Bon',
            roomId: '', locationLabel: '', unitPrice: null, isConsumable: false, notes: ''
        },
        createItemErrors: {},

        openCreateItem() {
            this.newItem = {
                name: '', code: '', categoryId: '', initialQuantity: 0, condition: 'Bon',
                roomId: '', locationLabel: '', unitPrice: null, isConsumable: false, notes: ''
            };
            this.createItemErrors = {};
            this.isCreateItemOpen = true;
        },

        closeCreateItem() { this.isCreateItemOpen = false; },

        async submitCreateItem() {
            this.isSavingItem = true;
            this.createItemErrors = {};
            try {
                await window.api.post('/inventory/items', {
                    name: this.newItem.name,
                    code: this.newItem.code || null,
                    categoryId: this.newItem.categoryId,
                    initialQuantity: Number(this.newItem.initialQuantity) || 0,
                    condition: this.newItem.condition,
                    roomId: this.newItem.roomId || null,
                    locationLabel: this.newItem.locationLabel || null,
                    unitPrice: this.toNullableNumber(this.newItem.unitPrice),
                    isConsumable: this.newItem.isConsumable,
                    notes: this.newItem.notes || null
                });
                this.isCreateItemOpen = false;
                toast.success('Bien créé.');
                await Promise.all([this.loadItems(), this.loadCategories()]);
            } catch (err) {
                this.createItemErrors = window.api.toFieldErrors(err, 'Erreur lors de la création du bien.');
            } finally {
                this.isSavingItem = false;
            }
        },

        // ------------------------------------------------------------ Biens : édition
        editingItem: null, // { id, name, code, categoryId, condition, roomId, locationLabel, unitPrice, isConsumable, notes, rowVersion }
        isSavingEditItem: false,
        editItemErrors: {},

        openEditItem(item) {
            this.editingItem = {
                id: item.id, name: item.name, code: item.code || '', categoryId: item.categoryId,
                condition: item.condition, roomId: item.roomId || '', locationLabel: item.locationLabel || '',
                unitPrice: item.unitPrice, isConsumable: item.isConsumable, notes: item.notes || '',
                rowVersion: item.rowVersion
            };
            this.editItemErrors = {};
        },

        closeEditItem() { this.editingItem = null; this.editItemErrors = {}; },

        async submitEditItem() {
            if (!this.editingItem) return;
            this.isSavingEditItem = true;
            this.editItemErrors = {};
            try {
                await window.api.put(`/inventory/items/${this.editingItem.id}`, {
                    name: this.editingItem.name,
                    code: this.editingItem.code || null,
                    categoryId: this.editingItem.categoryId,
                    condition: this.editingItem.condition,
                    roomId: this.editingItem.roomId || null,
                    locationLabel: this.editingItem.locationLabel || null,
                    unitPrice: this.toNullableNumber(this.editingItem.unitPrice),
                    isConsumable: this.editingItem.isConsumable,
                    notes: this.editingItem.notes || null,
                    rowVersion: this.editingItem.rowVersion
                });
                this.closeEditItem();
                toast.success('Bien modifié.');
                await this.loadItems();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editItemErrors = { global: "Ce bien vient d'être modifié par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadItems();
                } else {
                    this.editItemErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEditItem = false;
            }
        },

        // ------------------------------------------------------------ Biens : suppression
        deletingItem: null,
        isDeletingItem: false,
        deleteItemError: null,

        openDeleteItem(item) {
            this.deletingItem = { id: item.id, name: item.name, rowVersion: item.rowVersion };
            this.deleteItemError = null;
        },

        closeDeleteItem() { this.deletingItem = null; this.deleteItemError = null; },

        async confirmDeleteItem() {
            if (!this.deletingItem) return;
            this.isDeletingItem = true;
            this.deleteItemError = null;
            try {
                await window.api.delete(`/inventory/items/${this.deletingItem.id}?rowVersion=${this.deletingItem.rowVersion}`);
                this.deletingItem = null;
                toast.success('Bien archivé.');
                await this.loadItems();
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    this.deleteItemError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteItemError = "Ce bien vient d'être modifié par un autre utilisateur. Rafraîchissez la page puis réessayez.";
                    await this.loadItems();
                } else {
                    this.deleteItemError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingItem = false;
            }
        },

        // ------------------------------------------------------------ Biens : fiche détaillée
        detailItem: null, // InventoryItemDetailDto, ou null tant que fermé
        isLoadingDetail: false,
        detailError: null,

        async openItemDetail(item) {
            this.detailItem = { id: item.id, name: item.name }; // ouvre tout de suite, affiche le squelette
            this.detailError = null;
            this.isLoadingDetail = true;
            try {
                this.detailItem = await window.api.get(`/inventory/items/${item.id}`);
            } catch (err) {
                this.detailError = window.api.toMessage(err, 'Erreur lors du chargement de la fiche.');
            } finally {
                this.isLoadingDetail = false;
            }
        },

        closeItemDetail() { this.detailItem = null; this.detailError = null; },

        // ================================================================== ONGLET CATÉGORIES
        async loadCategories() {
            try {
                this.categories = await window.api.get('/inventory/categories');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des catégories.');
            }
        },

        isCreateCategoryOpen: false,
        isSavingCategory: false,
        newCategory: { name: '', description: '' },
        createCategoryErrors: {},

        openCreateCategory() {
            this.newCategory = { name: '', description: '' };
            this.createCategoryErrors = {};
            this.isCreateCategoryOpen = true;
        },

        closeCreateCategory() { this.isCreateCategoryOpen = false; },

        async submitCreateCategory() {
            this.isSavingCategory = true;
            this.createCategoryErrors = {};
            try {
                await window.api.post('/inventory/categories', this.newCategory);
                this.isCreateCategoryOpen = false;
                toast.success('Catégorie créée.');
                await this.loadCategories();
            } catch (err) {
                this.createCategoryErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la catégorie.');
            } finally {
                this.isSavingCategory = false;
            }
        },

        editingCategory: null,
        isSavingEditCategory: false,
        editCategoryErrors: {},

        openEditCategory(category) {
            this.editingCategory = { id: category.id, name: category.name, description: category.description || '', rowVersion: category.rowVersion };
            this.editCategoryErrors = {};
        },

        closeEditCategory() { this.editingCategory = null; this.editCategoryErrors = {}; },

        async submitEditCategory() {
            if (!this.editingCategory) return;
            this.isSavingEditCategory = true;
            this.editCategoryErrors = {};
            try {
                await window.api.put(`/inventory/categories/${this.editingCategory.id}`, this.editingCategory);
                this.closeEditCategory();
                toast.success('Catégorie modifiée.');
                await this.loadCategories();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editCategoryErrors = { global: "Cette catégorie vient d'être modifiée par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadCategories();
                } else {
                    this.editCategoryErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEditCategory = false;
            }
        },

        deletingCategory: null,
        isDeletingCategory: false,
        deleteCategoryError: null,

        openDeleteCategory(category) {
            this.deletingCategory = { id: category.id, name: category.name, rowVersion: category.rowVersion };
            this.deleteCategoryError = null;
        },

        closeDeleteCategory() { this.deletingCategory = null; this.deleteCategoryError = null; },

        async confirmDeleteCategory() {
            if (!this.deletingCategory) return;
            this.isDeletingCategory = true;
            this.deleteCategoryError = null;
            try {
                await window.api.delete(`/inventory/categories/${this.deletingCategory.id}?rowVersion=${this.deletingCategory.rowVersion}`);
                this.deletingCategory = null;
                toast.success('Catégorie archivée.');
                await this.loadCategories();
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    this.deleteCategoryError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteCategoryError = "Cette catégorie vient d'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.";
                    await this.loadCategories();
                } else {
                    this.deleteCategoryError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingCategory = false;
            }
        },

        // ================================================================== ONGLET MOUVEMENTS
        movements: [],
        movementsTotalCount: 0,
        movementsPage: 1,
        movementsPageSize: 20,
        isLoadingMovements: false,
        movementFilters: { itemId: '', type: '', from: '', to: '' },

        movementRequestTypeOptions: MOVEMENT_REQUEST_TYPE_OPTIONS,
        movementTypeFilterOptions: [{ value: '', label: 'Tous les types' }].concat(
            Object.entries(MOVEMENT_TYPE_LABELS).map(([value, label]) => ({ value, label }))),

        async loadMovements() {
            this.isLoadingMovements = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.movementsPage, pageSize: this.movementsPageSize });
                if (this.movementFilters.itemId) params.set('itemId', this.movementFilters.itemId);
                if (this.movementFilters.type) params.set('type', this.movementFilters.type);
                if (this.movementFilters.from) params.set('from', this.movementFilters.from);
                if (this.movementFilters.to) params.set('to', this.movementFilters.to);

                const data = await window.api.get(`/inventory/movements?${params.toString()}`);
                this.movements = (data && data.items) || [];
                this.movementsTotalCount = (data && data.totalCount) || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du journal de stock.');
            } finally {
                this.isLoadingMovements = false;
            }
        },

        applyMovementFilters() {
            this.movementsPage = 1;
            this.loadMovements();
        },

        resetMovementFilters() {
            this.movementFilters = { itemId: '', type: '', from: '', to: '' };
            this.applyMovementFilters();
        },

        // ------------------------------------------------------------ Nouveau mouvement
        isMovementModalOpen: false,
        isSavingMovement: false,
        movementForm: { itemId: '', type: 'Entree', quantity: 1, movementDate: '', reason: '', counterpartyLabel: '', rowVersion: 0 },
        movementErrors: {},

        /** Ouverture générique. `presetItem` (optionnel) préremplit le bien depuis une ligne du Catalogue. */
        async openMovementModal(presetItem) {
            this.movementForm = {
                itemId: presetItem ? presetItem.id : '', type: 'Entree', quantity: 1, movementDate: '',
                reason: '', counterpartyLabel: '', rowVersion: presetItem ? presetItem.rowVersion : 0
            };
            this.movementErrors = {};
            this.isMovementModalOpen = true;
            await this.loadPickerItems();
        },

        closeMovementModal() { this.isMovementModalOpen = false; },

        /** Le jeton rowVersion suit la SÉLECTION du bien : rechargé à chaque ouverture de modale (voir loadPickerItems). */
        onMovementItemSelected() {
            const item = this.pickerItems.find((i) => i.id === this.movementForm.itemId);
            this.movementForm.rowVersion = item ? item.rowVersion : 0;
        },

        get selectedMovementItem() {
            return this.pickerItems.find((i) => i.id === this.movementForm.itemId) || null;
        },

        async submitMovement() {
            this.isSavingMovement = true;
            this.movementErrors = {};
            try {
                await window.api.post('/inventory/movements', {
                    itemId: this.movementForm.itemId,
                    type: this.movementForm.type,
                    quantity: Number(this.movementForm.quantity) || 0,
                    movementDate: this.movementForm.movementDate || null,
                    reason: this.movementForm.reason,
                    counterpartyLabel: this.movementForm.counterpartyLabel || null,
                    rowVersion: this.movementForm.rowVersion
                });
                this.isMovementModalOpen = false;
                toast.success('Mouvement enregistré.');
                await Promise.all([this.loadItems(), this.tab === 'mouvements' ? this.loadMovements() : Promise.resolve()]);
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.movementErrors = { global: "Ce bien vient d'être modifié par un autre utilisateur — rouvrez le mouvement pour repartir d'une fiche à jour." };
                    await this.loadPickerItems();
                } else {
                    this.movementErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement du mouvement.");
                }
            } finally {
                this.isSavingMovement = false;
            }
        },

        // ================================================================== ONGLET PRÊTS & DÉCHARGES
        assignments: [],
        assignmentsTotalCount: 0,
        assignmentsPage: 1,
        assignmentsPageSize: 20,
        isLoadingAssignments: false,
        assignmentFilters: { status: '', overdueOnly: false },
        assignmentStatusFilterOptions: ASSIGNMENT_STATUS_FILTER_OPTIONS,

        async loadAssignments() {
            this.isLoadingAssignments = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.assignmentsPage, pageSize: this.assignmentsPageSize });
                if (this.assignmentFilters.status) params.set('status', this.assignmentFilters.status);
                if (this.assignmentFilters.overdueOnly) params.set('overdueOnly', 'true');

                const data = await window.api.get(`/inventory/assignments?${params.toString()}`);
                this.assignments = (data && data.items) || [];
                this.assignmentsTotalCount = (data && data.totalCount) || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des prêts.');
            } finally {
                this.isLoadingAssignments = false;
            }
        },

        applyAssignmentFilters() {
            this.assignmentsPage = 1;
            this.loadAssignments();
        },

        resetAssignmentFilters() {
            this.assignmentFilters = { status: '', overdueOnly: false };
            this.applyAssignmentFilters();
        },

        // ------------------------------------------------------------ Nouveau prêt
        isAssignmentModalOpen: false,
        isSavingAssignment: false,
        assignmentForm: {
            itemId: '', quantity: 1, beneficiaryType: 'Eleve', beneficiaryId: '',
            assignedOn: '', dueOn: '', notes: '', rowVersion: 0
        },
        assignmentErrors: {},

        async openAssignmentModal(presetItem) {
            this.assignmentForm = {
                itemId: presetItem ? presetItem.id : '', quantity: 1, beneficiaryType: 'Eleve', beneficiaryId: '',
                assignedOn: '', dueOn: '', notes: '', rowVersion: presetItem ? presetItem.rowVersion : 0
            };
            this.assignmentErrors = {};
            this.isAssignmentModalOpen = true;
            await Promise.all([this.loadPickerItems(), this.loadBeneficiaries()]);
        },

        closeAssignmentModal() { this.isAssignmentModalOpen = false; },

        onAssignmentItemSelected() {
            const item = this.pickerItems.find((i) => i.id === this.assignmentForm.itemId);
            this.assignmentForm.rowVersion = item ? item.rowVersion : 0;
        },

        /** Un changement de type de bénéficiaire invalide le choix précédent — un élève sélectionné n'a pas de sens une fois basculé sur « Personnel ». */
        onBeneficiaryTypeChanged() {
            this.assignmentForm.beneficiaryId = '';
        },

        get selectedAssignmentItem() {
            return this.pickerItems.find((i) => i.id === this.assignmentForm.itemId) || null;
        },

        async submitAssignment() {
            this.isSavingAssignment = true;
            this.assignmentErrors = {};
            try {
                await window.api.post('/inventory/assignments', {
                    itemId: this.assignmentForm.itemId,
                    quantity: Number(this.assignmentForm.quantity) || 0,
                    beneficiaryType: this.assignmentForm.beneficiaryType,
                    beneficiaryId: this.assignmentForm.beneficiaryId,
                    assignedOn: this.assignmentForm.assignedOn || null,
                    dueOn: this.assignmentForm.dueOn || null,
                    notes: this.assignmentForm.notes || null,
                    rowVersion: this.assignmentForm.rowVersion
                });
                this.isAssignmentModalOpen = false;
                toast.success('Prêt enregistré.');
                await Promise.all([
                    this.loadItems(),
                    this.tab === 'prets' ? this.loadAssignments() : Promise.resolve()
                ]);
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.assignmentErrors = { global: "Ce bien vient d'être modifié par un autre utilisateur — rouvrez le prêt pour repartir d'une fiche à jour." };
                    await this.loadPickerItems();
                } else {
                    this.assignmentErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement du prêt.");
                }
            } finally {
                this.isSavingAssignment = false;
            }
        },

        // ------------------------------------------------------------ Restitution
        returningAssignment: null, // { id, reference, itemName, outstanding, rowVersion }
        isSavingReturn: false,
        returnForm: { returnedQuantity: 0, returnCondition: 'Bon', returnedOn: '', declareRemainderLost: false },
        returnErrors: {},

        openReturnAssignment(assignment) {
            const outstanding = assignment.quantity - assignment.returnedQuantity;
            this.returningAssignment = {
                id: assignment.id, reference: assignment.reference, itemName: assignment.itemName,
                outstanding, rowVersion: assignment.rowVersion
            };
            this.returnForm = { returnedQuantity: outstanding, returnCondition: 'Bon', returnedOn: '', declareRemainderLost: false };
            this.returnErrors = {};
        },

        closeReturnAssignment() { this.returningAssignment = null; this.returnErrors = {}; },

        async submitReturn() {
            if (!this.returningAssignment) return;
            this.isSavingReturn = true;
            this.returnErrors = {};
            try {
                await window.api.post(`/inventory/assignments/${this.returningAssignment.id}/return`, {
                    returnedQuantity: Number(this.returnForm.returnedQuantity) || 0,
                    returnCondition: this.returnForm.returnCondition,
                    returnedOn: this.returnForm.returnedOn || null,
                    declareRemainderLost: this.returnForm.declareRemainderLost,
                    rowVersion: this.returningAssignment.rowVersion
                });
                this.returningAssignment = null;
                toast.success('Restitution enregistrée.');
                await Promise.all([this.loadItems(), this.loadAssignments()]);
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.returnErrors = { global: "Cette fiche vient d'être modifiée par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.loadAssignments();
                } else {
                    this.returnErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement de la restitution.");
                }
            } finally {
                this.isSavingReturn = false;
            }
        },

        // ------------------------------------------------------------ Annulation
        cancellingAssignment: null,
        isCancellingAssignment: false,
        cancelAssignmentError: null,

        openCancelAssignment(assignment) {
            this.cancellingAssignment = { id: assignment.id, reference: assignment.reference, itemName: assignment.itemName, rowVersion: assignment.rowVersion };
            this.cancelAssignmentError = null;
        },

        closeCancelAssignment() { this.cancellingAssignment = null; this.cancelAssignmentError = null; },

        async confirmCancelAssignment() {
            if (!this.cancellingAssignment) return;
            this.isCancellingAssignment = true;
            this.cancelAssignmentError = null;
            try {
                await window.api.delete(`/inventory/assignments/${this.cancellingAssignment.id}?rowVersion=${this.cancellingAssignment.rowVersion}`);
                this.cancellingAssignment = null;
                toast.success('Fiche de prêt annulée.');
                await Promise.all([this.loadItems(), this.loadAssignments()]);
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    this.cancelAssignmentError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.cancelAssignmentError = "Cette fiche vient d'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.";
                    await this.loadAssignments();
                } else {
                    this.cancelAssignmentError = (err && err.message) || "Erreur lors de l'annulation.";
                }
            } finally {
                this.isCancellingAssignment = false;
            }
        },

        // ------------------------------------------------------------ Impression de la décharge
        async printDischargeNote(assignment) {
            await this.openPdfPreview(
                `/api/v1/inventory/assignments/${assignment.id}/pdf`,
                `Décharge ${assignment.reference}`,
                `Decharge-${assignment.reference}.pdf`);
        },

        // ================================================================== FICHE D'INVENTAIRE GLOBALE
        /** Reprend les filtres catégorie/salle actifs sur le Catalogue — l'export reflète ce que l'écran affiche. */
        async printInventoryReport() {
            const params = new URLSearchParams();
            if (this.itemFilters.categoryId) params.set('categoryId', this.itemFilters.categoryId);
            if (this.itemFilters.roomId) params.set('roomId', this.itemFilters.roomId);
            const query = params.toString();

            await this.openPdfPreview(
                `/api/v1/inventory/reports/global/pdf${query ? `?${query}` : ''}`,
                "Fiche d'inventaire",
                'Fiche-Inventaire.pdf');
        }
    }));
});
