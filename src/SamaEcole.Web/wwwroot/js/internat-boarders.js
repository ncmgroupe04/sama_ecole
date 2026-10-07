/**
 * Onglet « Pensionnaires » de l'écran Internat (/internat) — liste, filtres, fiche et profil d'un pensionnaire, sur /api/v1/boarding (lot G4, tâche 8).
 *
 * Données : GET /boarding/boarders (pavillon, chambre, régime, statut active|awaitingBed|onLeave|ended, recherche, pagination) ; GET /boarding/boarders/{id}
 * (fiche) ; PUT /boarding/boarders/{id}/profile (contact d'urgence, personnes habilitées, fiche médicale) avec le `rowVersion` du SÉJOUR.
 *
 * FICHE MÉDICALE — donnée de santé d'un mineur. C'est le serveur qui juge (BoarderDetailReader.CanReadMedical : Directeur et Surveillant ; il masque
 * `medicalNotes` et répond 403 à un autre rôle qui en enverrait une valeur). L'écran ne fait que REFLÉTER ce choix : la section n'est rendue que pour
 * ces deux rôles (`x-if`, donc absente du DOM pour les autres) et `medicalNotes` n'est JAMAIS mis dans le corps du PUT pour les autres. Un rôle qui n'envoie
 * pas la clé conserve la fiche existante côté serveur.
 *
 * La logique pure vit dans `window.boardersLogic`, testée sans navigateur (tests/js/internat-boarders.test.mjs).
 */
window.boardersLogic = (() => {
    const MEDICAL_ROLES = ['Directeur', 'Surveillant'];
    const MAX_PERSONS = 10;
    const MAX_MEDICAL = 2000;

    /** Statuts de GET /boarding/boarders?status=… (valeurs de l'API), dans l'ordre d'affichage. */
    const STATUS_CHOICES = [
        { value: 'active', label: 'Actifs' },
        { value: 'awaitingBed', label: 'En attente de lit' },
        { value: 'onLeave', label: 'En permission' },
        { value: 'ended', label: 'Séjours clos' }
    ];

    const REGIME_CHOICES = [
        { value: '', label: 'Tous les régimes' },
        { value: 'Interne', label: 'Interne' },
        { value: 'DemiPensionnaire', label: 'Demi-pensionnaire' }
    ];

    /** Miroir d'affichage de BoarderDetailReader.CanReadMedical. Confort uniquement : le serveur reste la barrière. */
    function canSeeMedical(role) {
        return MEDICAL_ROLES.includes(role);
    }

    /** Texte d'une fiche médicale : une fiche absente ou vide se lit « Non renseignée », jamais « null ». */
    function medicalLabel(notes) {
        return typeof notes === 'string' && notes.trim() !== '' ? notes : 'Non renseignée';
    }

    function regimeLabel(regime) {
        if (regime === 'Interne') return 'Interne';
        if (regime === 'DemiPensionnaire') return 'Demi-pensionnaire';
        return regime || '';
    }

    /** Situation d'une ligne de la liste. Un demi-pensionnaire n'a pas de lit et n'en attend pas. */
    function statusLabel(item) {
        if (!item.isActive) return 'Séjour clos';
        if (item.regime === 'Interne' && !item.bedId) return 'En attente de lit';
        return 'Actif';
    }

    /** Emplacement lisible : « Pavillon — Chambre — Lit n ». Vide pour un séjour sans lit. */
    function locationLabel(item) {
        if (!item.bedId) return '';
        return `${item.dormitoryName} — ${item.roomName} — Lit ${item.bedNumber}`;
    }

    /** Chaîne de requête de GET /boarding/boarders. Les filtres vides sont omis ; une page invalide retombe sur 1. */
    function buildListQuery(filters, page, pageSize) {
        const parts = [];
        const add = (key, value) => {
            if (value !== undefined && value !== null && String(value).trim() !== '') parts.push(`${key}=${encodeURIComponent(String(value).trim())}`);
        };
        add('dormitoryId', filters.dormitoryId);
        add('roomId', filters.roomId);
        add('regime', filters.regime);
        add('status', filters.status);
        add('search', filters.search);
        const safePage = Number.isInteger(page) && page >= 1 ? page : 1;
        parts.push(`page=${safePage}`);
        parts.push(`pageSize=${pageSize}`);
        return parts.join('&');
    }

    function blankToNull(value) {
        const text = typeof value === 'string' ? value.trim() : '';
        return text === '' ? null : text;
    }

    function blankPerson() {
        return { name: '', relationship: '', phone: '' };
    }

    /** Formulaire de modification, copié de la fiche : l'édition ne touche jamais l'objet affiché tant qu'on n'a pas enregistré. */
    function formFromDetail(detail) {
        return {
            medicalNotes: detail.medicalNotes ?? '',
            emergencyContactName: detail.emergencyContactName ?? '',
            emergencyContactPhone: detail.emergencyContactPhone ?? '',
            allowedExitPersons: (detail.allowedExitPersons || []).map((p) => ({ name: p.name, relationship: p.relationship, phone: p.phone }))
        };
    }

    /**
     * Corps de PUT /boarding/boarders/{id}/profile. `medicalNotes` n'est inclus QUE pour un rôle autorisé (la clé est absente, pas nulle : le serveur
     * conserve alors la fiche existante). Pour un rôle autorisé, une fiche vide est envoyée en `null` : le serveur l'efface.
     * `rowVersion` 0 est un jeton valide.
     */
    function buildProfileBody(form, rowVersion, role) {
        const body = {
            emergencyContactName: blankToNull(form.emergencyContactName),
            emergencyContactPhone: blankToNull(form.emergencyContactPhone),
            allowedExitPersons: (form.allowedExitPersons || []).map((p) => ({
                name: String(p.name || '').trim(),
                relationship: String(p.relationship || '').trim(),
                phone: String(p.phone || '').trim()
            })),
            rowVersion
        };
        if (canSeeMedical(role)) body.medicalNotes = blankToNull(form.medicalNotes);
        return body;
    }

    function tooLong(value, max) {
        return typeof value === 'string' && value.length > max;
    }

    /** Validation de confort (les bornes sont celles de UpdateBoarderProfileCommandValidator) ; objet vide = valide. */
    function validateProfile(form) {
        const errors = {};
        const persons = form.allowedExitPersons || [];

        if (persons.length > MAX_PERSONS) errors.allowedExitPersons = `Dix personnes habilitées au plus.`;
        else if (persons.some((p) => String(p.name || '').trim() === '')) errors.allowedExitPersons = 'Chaque personne habilitée doit avoir un nom.';
        else if (persons.some((p) => tooLong(p.name, 150) || tooLong(p.relationship, 50) || tooLong(p.phone, 30))) {
            errors.allowedExitPersons = 'Nom (150), lien de parenté (50) ou téléphone (30) trop long.';
        }

        if (tooLong(form.medicalNotes, MAX_MEDICAL)) errors.medicalNotes = `La fiche médicale ne doit pas dépasser ${MAX_MEDICAL} caractères.`;
        if (tooLong(form.emergencyContactName, 150)) errors.emergencyContactName = 'Le nom du contact ne doit pas dépasser 150 caractères.';
        if (tooLong(form.emergencyContactPhone, 30)) errors.emergencyContactPhone = 'Le téléphone ne doit pas dépasser 30 caractères.';

        return errors;
    }

    return {
        MAX_PERSONS, STATUS_CHOICES, REGIME_CHOICES,
        canSeeMedical, medicalLabel, regimeLabel, statusLabel, locationLabel,
        buildListQuery, blankPerson, formFromDetail, buildProfileBody, validateProfile
    };
})();

document.addEventListener('alpine:init', () => {
    const PAGE_SIZE = 20;
    const SEARCH_DEBOUNCE_MS = 300;

    const emptyPanel = () => ({
        open: false, loading: false, data: null, error: '', editing: false, saving: false, form: null, errors: {}
    });

    Alpine.data('boardersTab', () => ({
        // Modale d'aperçu PDF commune (wwwroot/js/pdf-preview.js) : fiche du pensionnaire.
        ...window.pdfPreview.state(),

        loading: true,
        error: '',
        filterError: '',
        notice: '',
        items: [],
        totalCount: 0,
        page: 1,
        pageSize: PAGE_SIZE,
        filters: { dormitoryId: '', roomId: '', regime: '', status: 'active', search: '' },
        dormitories: [],
        rooms: [],
        role: '',
        statusChoices: window.boardersLogic.STATUS_CHOICES,
        regimeChoices: window.boardersLogic.REGIME_CHOICES,
        panel: emptyPanel(),
        searchDebounce: null,
        listSeq: 0,
        detailSeq: 0,

        async init() {
            this.role = window.auth.role;
            // Les pavillons alimentent le filtre : un échec n'empêche pas la liste (le filtre reste simplement vide).
            const dormitories = this.loadDormitories();
            await Promise.all([this.load(), dormitories]);
        },

        helpers() { return window.internatHelpers; },
        logic() { return window.boardersLogic; },

        get showMedical() { return this.logic().canSeeMedical(this.role); },
        get medicalText() { return this.logic().medicalLabel(this.panel.data ? this.panel.data.medicalNotes : null); },
        get pageCount() { return Math.max(1, Math.ceil(this.totalCount / this.pageSize)); },
        get hasPrevious() { return this.page > 1; },
        get hasNext() { return this.page < this.pageCount; },
        get dormitoryOptions() { return [{ value: '', label: 'Tous les pavillons' }, ...this.dormitories.map((d) => ({ value: d.id, label: d.name }))]; },
        get roomOptions() { return [{ value: '', label: 'Toutes les chambres' }, ...this.rooms.map((r) => ({ value: r.id, label: r.name }))]; },
        get canAddPerson() { return !!this.panel.form && this.panel.form.allowedExitPersons.length < this.logic().MAX_PERSONS; },

        statusLabel(item) { return this.logic().statusLabel(item); },
        regimeLabel(regime) { return this.logic().regimeLabel(regime); },
        locationLabel(item) { return this.logic().locationLabel(item); },
        leaveReasonLabel(reason) { return this.helpers().leaveReasonLabel(reason); },
        leaveStatusLabel(status) { return this.helpers().leaveStatusLabel(status); },

        // ------------------------------------------------------------------ Liste

        async loadDormitories() {
            this.filterError = '';
            try {
                this.dormitories = await window.api.get('/boarding/dormitories');
            } catch (err) {
                this.dormitories = [];
                this.filterError = this.helpers().errorMessage(err, "Les pavillons n'ont pas pu être chargés : le filtre par pavillon est indisponible.");
            }
        },

        /**
         * Charge la page courante. Un compteur écarte toute réponse arrivée après une demande plus récente (frappes rapides, changement de filtre) :
         * sans lui, une liste lente écraserait la bonne.
         */
        async load() {
            const seq = ++this.listSeq;
            this.loading = true;
            this.error = '';
            try {
                const result = await window.api.get(`/boarding/boarders?${this.logic().buildListQuery(this.filters, this.page, this.pageSize)}`);
                if (seq !== this.listSeq) return;
                this.items = result.items;
                this.totalCount = result.totalCount;
            } catch (err) {
                if (seq !== this.listSeq) return;
                this.items = [];
                this.totalCount = 0;
                this.error = this.helpers().errorMessage(err, 'Erreur lors du chargement des pensionnaires.');
            } finally {
                if (seq === this.listSeq) this.loading = false;
            }
        },

        async applyFilters() {
            this.page = 1;
            await this.load();
        },

        /** Changer de pavillon réinitialise la chambre (elle appartient à l'ancien pavillon) puis charge les chambres du nouveau. */
        async onDormitoryChange() {
            this.filters.roomId = '';
            this.rooms = [];
            if (this.filters.dormitoryId) {
                try {
                    const detail = await window.api.get(`/boarding/dormitories/${this.filters.dormitoryId}`);
                    this.rooms = detail.rooms || [];
                } catch (err) {
                    this.rooms = [];
                    this.filterError = this.helpers().errorMessage(err, "Les chambres n'ont pas pu être chargées : le filtre par chambre est indisponible.");
                }
            }
            await this.applyFilters();
        },

        onSearchInput() {
            clearTimeout(this.searchDebounce);
            this.searchDebounce = setTimeout(() => this.applyFilters(), SEARCH_DEBOUNCE_MS);
        },

        async goToPage(page) {
            if (!Number.isInteger(page) || page < 1 || page > this.pageCount) return;
            this.page = page;
            await this.load();
        },

        // ------------------------------------------------------------------ Fiche

        async openDetail(item) {
            const seq = ++this.detailSeq;
            this.panel = { ...emptyPanel(), open: true, loading: true };
            try {
                const detail = await window.api.get(`/boarding/boarders/${item.id}`);
                if (seq !== this.detailSeq) return;
                this.panel.data = detail;
            } catch (err) {
                if (seq !== this.detailSeq) return;
                this.panel.error = this.helpers().errorMessage(err, 'Erreur lors du chargement de la fiche.');
            } finally {
                if (seq === this.detailSeq) this.panel.loading = false;
            }
        },

        closeDetail() {
            this.detailSeq++;
            this.panel = emptyPanel();
        },

        // ------------------------------------------------------------------ PDF

        /**
         * Fiche individuelle en PDF, pour les trois rôles de lecture. Aucun paramètre n'est envoyé : c'est le serveur qui retire la fiche médicale du
         * document pour le Secrétariat, l'écran ne choisit jamais ce que le PDF contient.
         */
        async openBoarderPdf() {
            const boarder = this.panel.data && this.panel.data.boarder;
            if (!boarder) return;
            const helpers = this.helpers();
            await this.openPdfPreview(
                helpers.boarderPdfUrl(boarder.id),
                `Fiche de ${boarder.studentName}`,
                helpers.pdfFileName('Fiche-Pensionnaire', boarder.matricule));
        },

        // ------------------------------------------------------------------ Modification du profil

        startEdit() {
            if (!this.panel.data) return;
            this.panel.form = this.logic().formFromDetail(this.panel.data);
            this.panel.errors = {};
            this.panel.error = '';
            this.panel.editing = true;
        },

        cancelEdit() {
            this.panel.editing = false;
            this.panel.form = null;
            this.panel.errors = {};
        },

        addPerson() {
            if (this.canAddPerson) this.panel.form.allowedExitPersons.push(this.logic().blankPerson());
        },

        removePerson(index) {
            this.panel.form.allowedExitPersons.splice(index, 1);
        },

        async saveProfile() {
            const panel = this.panel;
            if (!panel.editing || !panel.data) return;

            panel.errors = this.logic().validateProfile(panel.form);
            if (Object.keys(panel.errors).length > 0) return;

            panel.saving = true;
            panel.error = '';
            const boarderId = panel.data.boarder.id;
            try {
                const body = this.logic().buildProfileBody(panel.form, panel.data.boarder.rowVersion, this.role);
                const updated = await window.api.put(`/boarding/boarders/${boarderId}/profile`, body);
                panel.data = updated;
                panel.editing = false;
                panel.form = null;
                this.notice = 'Fiche enregistrée.';
                this.items = this.items.map((i) => (i.id === updated.boarder.id ? updated.boarder : i));
            } catch (err) {
                // Un refus déjà montré par la modale « Accès refusé » rend un message vide : la saisie reste ouverte, rien n'est doublé.
                panel.error = this.helpers().errorMessage(err, "Impossible d'enregistrer la fiche.");
                if (err && err.code === 'CONCURRENCY_CONFLICT') {
                    // Quelqu'un a modifié le séjour entre-temps : on affiche la version à jour plutôt que d'écraser son travail.
                    panel.editing = false;
                    panel.form = null;
                    try {
                        panel.data = await window.api.get(`/boarding/boarders/${boarderId}`);
                    } catch { /* la fiche affichée reste l'ancienne ; le message de conflit est déjà visible */ }
                }
            } finally {
                panel.saving = false;
            }
        }
    }));
});
