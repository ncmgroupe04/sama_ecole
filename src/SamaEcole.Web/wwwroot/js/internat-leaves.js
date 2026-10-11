/**
 * Onglet « Sorties » de l'écran Internat (/internat) — registre, déclaration d'une sortie et enregistrement du retour, sur /api/v1/boarding (lot G4, tâche 10).
 *
 * Données : GET /boarding/leaves (statut Pending|Active|Overdue|Returned, pavillon, période, accompagnateur non habilité, pagination) ;
 * POST /boarding/leaves (déclaration) ; PUT /boarding/leaves/{id}/return (retour, avec le `rowVersion` de la SORTIE) ;
 * GET /boarding/boarders?status=active&search= (choix du pensionnaire) puis GET /boarding/boarders/{id} (ses personnes habilitées).
 *
 * ACCOMPAGNATEUR HORS LISTE = AVERTISSEMENT, JAMAIS UN BLOCAGE (spec Q5) : refuser de laisser partir un enfant avec un proche non enregistré serait
 * dangereux lors d'une urgence de santé. L'écran signale l'écart avant l'envoi avec la MÊME règle que le serveur (`CompanionMatcher` : espaces en trop et casse
 * ignorés, nom complet identique, « Fatou » ne correspond pas à « Fatou Diop »). Le serveur reste le juge : il fige `isCompanionUnlisted` à la déclaration,
 * l'écran ne l'envoie jamais.
 *
 * La logique pure vit dans `window.leavesLogic`, testée sans navigateur (tests/js/internat-leaves.test.mjs).
 */
window.leavesLogic = (() => {
    const MAX_COMPANION = 150;
    const MAX_DETAIL = 500;

    /** Filtre de statut du registre (valeurs de `LeaveStatus` côté API). */
    const STATUS_FILTERS = [
        { value: '', label: 'Toutes' },
        { value: 'Pending', label: 'À venir' },
        { value: 'Active', label: 'En cours' },
        { value: 'Overdue', label: 'En retard' },
        { value: 'Returned', label: 'Rentrées' }
    ];

    /** Même normalisation que CompanionMatcher.Normalize : espaces de bord retirés, suites d'espaces réduites à un seul. */
    function normalizeName(value) {
        return String(value || '').trim().replace(/\s+/g, ' ');
    }

    /**
     * Miroir de CompanionMatcher.IsListed : vrai si l'accompagnateur correspond à une personne habilitée. Casse ignorée (É = é), accents conservés
     * (« Elodie » ≠ « Élodie »), pas de correspondance partielle, un nom vide n'est jamais habilité.
     */
    function isCompanionListed(accompaniedBy, persons) {
        const wanted = normalizeName(accompaniedBy).toLocaleLowerCase('fr');
        if (wanted === '') return false;
        return (persons || []).some((p) => normalizeName(p.name).toLocaleLowerCase('fr') === wanted);
    }

    /** Chaîne de requête de GET /boarding/leaves ; filtres vides omis, `unlistedOnly` seulement quand il est coché. */
    function buildLeaveListQuery(filters, page, pageSize) {
        const parts = [];
        const add = (key, value) => {
            if (value !== undefined && value !== null && String(value).trim() !== '') parts.push(`${key}=${encodeURIComponent(String(value).trim())}`);
        };
        add('dormitoryId', filters.dormitoryId);
        add('status', filters.status);
        add('from', filters.from);
        add('to', filters.to);
        if (filters.unlistedOnly) parts.push('unlistedOnly=true');
        parts.push(`page=${Number.isInteger(page) && page >= 1 ? page : 1}`);
        parts.push(`pageSize=${pageSize}`);
        return parts.join('&');
    }

    function blankToNull(value) {
        const text = typeof value === 'string' ? value.trim() : '';
        return text === '' ? null : text;
    }

    /** Corps de POST /boarding/leaves. `isCompanionUnlisted` n'y figure pas : il est calculé et figé par le serveur. */
    function buildDeclareBody(form, boarderId) {
        return {
            boarderId,
            leaveDate: form.leaveDate,
            expectedReturnDate: form.expectedReturnDate,
            reason: form.reason,
            reasonDetail: blankToNull(form.reasonDetail),
            accompaniedBy: String(form.accompaniedBy || '').trim()
        };
    }

    /** Erreurs par champ ; objet vide = valide. Mêmes bornes que DeclareLeaveCommandValidator. */
    function validateDeclare(form, boarderId) {
        const errors = {};
        if (!boarderId) errors.boarder = 'Choisissez un pensionnaire.';
        if (!form.leaveDate) errors.leaveDate = 'Indiquez la date de sortie.';
        if (!form.expectedReturnDate) errors.expectedReturnDate = 'Indiquez la date de retour prévue.';
        else if (form.leaveDate && form.expectedReturnDate < form.leaveDate) errors.expectedReturnDate = 'Le retour prévu ne peut pas précéder la sortie.';

        const companion = String(form.accompaniedBy || '').trim();
        if (companion === '') errors.accompaniedBy = 'Indiquez qui accompagne le pensionnaire.';
        else if (companion.length > MAX_COMPANION) errors.accompaniedBy = `Le nom ne doit pas dépasser ${MAX_COMPANION} caractères.`;

        if (typeof form.reasonDetail === 'string' && form.reasonDetail.length > MAX_DETAIL) errors.reasonDetail = `Le détail ne doit pas dépasser ${MAX_DETAIL} caractères.`;
        return errors;
    }

    /** Corps de PUT /boarding/leaves/{id}/return. `rowVersion` 0 est un jeton valide. */
    function buildReturnBody(leave, actualReturnDate) {
        return { rowVersion: leave.rowVersion, actualReturnDate };
    }

    /** Un retour s'enregistre tant que la sortie n'est pas rentrée (y compris « à venir » : le départ n'a pas eu lieu mais le serveur tranche). */
    function canRecordReturn(leave) {
        return leave.status !== 'Returned' && !leave.actualReturnDate;
    }

    return {
        STATUS_FILTERS, normalizeName, isCompanionListed, buildLeaveListQuery, buildDeclareBody, validateDeclare, buildReturnBody, canRecordReturn
    };
})();

document.addEventListener('alpine:init', () => {
    const PAGE_SIZE = 20;
    const MIN_TERM = 2;
    const SEARCH_DEBOUNCE_MS = 300;

    const blankForm = (helpers) => ({
        leaveDate: helpers.today(), expectedReturnDate: helpers.today(), reason: 'Weekend', reasonDetail: '', accompaniedBy: ''
    });

    const blankDeclare = (helpers) => ({
        open: false, search: '', results: [], boarder: null, persons: [], form: blankForm(helpers), errors: {}, error: '', saving: false
    });

    const blankReturn = () => ({ open: false, leave: null, date: '', error: '', saving: false, stale: false });

    Alpine.data('leavesTab', () => ({
        loading: true,
        loadedAt: 0,
        error: '',
        filterError: '',
        notice: '',
        items: [],
        totalCount: 0,
        page: 1,
        pageSize: PAGE_SIZE,
        filters: { status: '', dormitoryId: '', from: '', to: '', unlistedOnly: false },
        dormitories: [],
        statusFilters: window.leavesLogic.STATUS_FILTERS,
        reasonChoices: window.internatHelpers.LEAVE_REASONS,
        declare: blankDeclare(window.internatHelpers),
        returnModal: blankReturn(),
        searchDebounce: null,
        listSeq: 0,
        searchSeq: 0,

        async init() {
            const dormitories = this.loadDormitories();
            await Promise.all([this.load(), dormitories]);
        },

        helpers() { return window.internatHelpers; },
        logic() { return window.leavesLogic; },

        get pageCount() { return Math.max(1, Math.ceil(this.totalCount / this.pageSize)); },
        get dormitoryOptions() { return [{ value: '', label: 'Tous les pavillons' }, ...this.dormitories.map((d) => ({ value: d.id, label: d.name }))]; },

        statusLabel(status) { return this.helpers().leaveStatusLabel(status); },
        reasonLabel(reason) { return this.helpers().leaveReasonLabel(reason); },
        canRecordReturn(leave) { return this.logic().canRecordReturn(leave); },

        // ------------------------------------------------------------------ Registre

        async loadDormitories() {
            this.filterError = '';
            try {
                this.dormitories = await window.api.get('/boarding/dormitories');
            } catch (err) {
                this.dormitories = [];
                this.filterError = this.helpers().errorMessage(err, "Les pavillons n'ont pas pu être chargés : le filtre par pavillon est indisponible.");
            }
        },

        /** Un compteur écarte toute réponse arrivée après une demande plus récente : une liste lente n'écrase jamais la bonne. */
        async load() {
            const seq = ++this.listSeq;
            this.loading = true;
            this.error = '';
            try {
                const result = await window.api.get(`/boarding/leaves?${this.logic().buildLeaveListQuery(this.filters, this.page, this.pageSize)}`);
                if (seq !== this.listSeq) return;
                this.items = result.items;
                this.totalCount = result.totalCount;
            } catch (err) {
                if (seq !== this.listSeq) return;
                this.items = [];
                this.totalCount = 0;
                this.error = this.helpers().errorMessage(err, 'Erreur lors du chargement du registre des sorties.');
            } finally {
                if (seq === this.listSeq) {
                    this.loading = false;
                    this.loadedAt = Date.now();
                }
            }
        },

        /** Relit à l'ouverture de l'onglet (registre ET pavillons du filtre), sauf modale de déclaration ou de retour ouverte, ou enregistrement en cours. */
        async onTabShown(key) {
            const busy = this.loading || this.declare.open || this.declare.saving || this.returnModal.open || this.returnModal.saving;
            if (!this.helpers().shouldRefreshOnShow({ tabKey: 'sorties', shownKey: key, busy, loadedAt: this.loadedAt, now: Date.now() })) return;
            await Promise.all([this.load(), this.loadDormitories()]);
        },

        async applyFilters() {
            this.page = 1;
            await this.load();
        },

        // ------------------------------------------------------------------ Déclaration

        openDeclare() {
            this.declare = { ...blankDeclare(this.helpers()), open: true };
        },

        closeDeclare() {
            clearTimeout(this.searchDebounce);
            this.searchSeq++;
            this.declare.open = false;
        },

        /** Debounce 300 ms. Modifier le texte après avoir choisi un pensionnaire invalide la sélection (sinon on déclarerait pour un autre que celui affiché). */
        onBoarderSearchInput() {
            const declare = this.declare;
            if (declare.boarder && declare.search !== declare.boarder.studentName) {
                declare.boarder = null;
                declare.persons = [];
            }
            clearTimeout(this.searchDebounce);
            this.searchDebounce = setTimeout(() => this.searchBoarders(), SEARCH_DEBOUNCE_MS);
        },

        async searchBoarders() {
            const term = this.declare.search.trim();
            const seq = ++this.searchSeq;
            if (term.length < MIN_TERM) {
                this.declare.results = [];
                return;
            }
            try {
                const page = await window.api.get(`/boarding/boarders?status=active&search=${encodeURIComponent(term)}&page=1&pageSize=10`);
                if (seq === this.searchSeq) this.declare.results = page.items;
            } catch {
                if (seq === this.searchSeq) this.declare.results = [];
            }
        },

        /** Choisit le pensionnaire et lit sa fiche pour connaître ses personnes habilitées (la liste de recherche ne les porte pas). */
        async selectBoarder(boarder) {
            const declare = this.declare;
            declare.boarder = boarder;
            declare.results = [];
            declare.search = boarder.studentName;
            declare.persons = [];
            declare.error = '';
            try {
                const detail = await window.api.get(`/boarding/boarders/${boarder.id}`);
                if (declare.boarder && declare.boarder.id === boarder.id) declare.persons = detail.allowedExitPersons || [];
            } catch (err) {
                // Sans la liste, l'écran ne peut pas comparer : il le dit plutôt que d'afficher à tort « habilité » ou « non habilité ».
                declare.error = this.helpers().errorMessage(err, "Les personnes habilitées n'ont pas pu être chargées : l'avertissement d'accompagnateur est indisponible.");
            }
        },

        /** Avertissement (jamais un blocage) : l'accompagnateur saisi ne figure pas parmi les personnes habilitées du pensionnaire choisi. */
        get companionUnlisted() {
            const declare = this.declare;
            const typed = declare.form.accompaniedBy.trim();
            return !!declare.boarder && typed !== '' && !this.logic().isCompanionListed(typed, declare.persons);
        },

        get canSubmitDeclare() { return !this.declare.saving && !!this.declare.boarder; },

        async submitDeclare() {
            const declare = this.declare;
            declare.errors = this.logic().validateDeclare(declare.form, declare.boarder ? declare.boarder.id : null);
            if (Object.keys(declare.errors).length > 0) return;

            declare.saving = true;
            declare.error = '';
            try {
                await window.api.post('/boarding/leaves', this.logic().buildDeclareBody(declare.form, declare.boarder.id));
                this.notice = `Sortie de ${declare.boarder.studentName} enregistrée.`;
                this.closeDeclare();
                await this.load();
            } catch (err) {
                declare.error = this.helpers().errorMessage(err, "Impossible d'enregistrer la sortie.");
            } finally {
                declare.saving = false;
            }
        },

        // ------------------------------------------------------------------ Retour

        openReturn(leave) {
            this.returnModal = { ...blankReturn(), open: true, leave, date: this.helpers().today() };
        },

        closeReturn() {
            this.returnModal.open = false;
        },

        async submitReturn() {
            const modal = this.returnModal;
            if (!modal.date) {
                modal.error = 'Indiquez la date de retour.';
                return;
            }

            modal.saving = true;
            modal.error = '';
            try {
                await window.api.put(`/boarding/leaves/${modal.leave.id}/return`, this.logic().buildReturnBody(modal.leave, modal.date));
                this.notice = `Retour de ${modal.leave.studentName} enregistré.`;
                this.closeReturn();
                await this.load();
            } catch (err) {
                modal.error = this.helpers().errorMessage(err, "Impossible d'enregistrer le retour.");
                // Retour déjà saisi ou sortie modifiée par un autre : le jeton est périmé, on relit le registre et on bloque la confirmation.
                if (err && (err.code === 'LEAVE_ALREADY_RETURNED' || err.code === 'CONCURRENCY_CONFLICT')) {
                    modal.stale = true;
                    await this.load();
                }
            } finally {
                modal.saving = false;
            }
        }
    }));
});
