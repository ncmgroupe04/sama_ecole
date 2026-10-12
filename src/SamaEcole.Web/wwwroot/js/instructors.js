/**
 * Gestion des Oustaz (/oustaz) — la Direction crée et modifie les fiches des maîtres coraniques, les suspend ou les
 * réactive, et leur lie le compte de connexion qui leur ouvre leur Halqa sur la tablette (/halqa).
 *
 * Consomme GET/POST/PUT /api/v1/internat/instructors (écriture réservée au Directeur côté serveur ; la lecture est
 * ouverte au Secrétariat et au Surveillant, qui voient la liste sans les actions) et GET /api/v1/users (Directeur
 * seulement) pour proposer les comptes « Enseignant » rattachables — même mécanisme que l'écran Enseignants.
 *
 * Le serveur décide de tout ce qui compte : un compte déjà lié à un autre Oustaz, ou d'un autre rôle, est refusé
 * (422) ; un compte d'une autre école renvoie un 404 neutre. La liste de comptes proposée ici n'est qu'un CONFORT
 * (elle écarte d'avance les comptes inutilisables) — jamais une garde.
 *
 * VERROU OPTIMISTE : chaque fiche porte son jeton xmin (`rowVersion`), renvoyé tel quel à la modification. Un 409
 * signifie qu'une autre personne a modifié la fiche : on le dit, et on relit la liste pour que la réouverture de la
 * fiche présente la version à jour.
 *
 * AFFECTATION DES ÉLÈVES (Directeur seul) : la fenêtre « Élèves » d'un Oustaz liste sa Halqa (GET instructors/{id}/students),
 * propose les élèves à lui rattacher (GET assignable-students : sans Oustaz par défaut, ou tous, avec recherche et pages) et
 * écrit par POST students/assign-instructor — 200 élèves au plus par envoi, tout ou rien côté serveur ; « Retirer » détache
 * (instructorId null) sans rien supprimer. Un élève d'une autre Halqa reste affectable : il est déplacé, et l'écran le dit.
 *
 * La logique pure (filtre, comptes rattachables, corps envoyés) vit dans `window.instructorsLogic`, testée sans
 * navigateur (tests/js/instructors.test.mjs).
 */
(function () {
    'use strict';

    const STATUS_LABELS = { Active: 'Actif', Suspended: 'Suspendu', Blocked: 'Bloqué' };

    function statusLabel(status) {
        return STATUS_LABELS[status] || status || '';
    }

    function blankToNull(value) {
        const text = typeof value === 'string' ? value.trim() : '';
        return text === '' ? null : text;
    }

    /** Filtre la liste côté client (une école compte quelques dizaines d'Oustaz, jamais des milliers). */
    function filterInstructors(instructors, search, status) {
        const term = (search || '').trim().toLowerCase();
        return (instructors || []).filter((i) => {
            if (status && i.status !== status) return false;
            if (term === '') return true;
            return [i.fullName, i.fullNameAr, i.phone, i.userEmail]
                .some((field) => typeof field === 'string' && field.toLowerCase().includes(term));
        });
    }

    /**
     * Comptes proposés dans le sélecteur : rôle Enseignant, actifs, et pas déjà liés à UN AUTRE Oustaz. Le compte
     * actuellement lié à la fiche en cours d'édition reste proposé (sinon on ne pourrait pas le conserver). Confort
     * d'affichage : le serveur refuse de toute façon un compte inutilisable.
     */
    function eligibleAccounts(users, instructors, ownUserId) {
        const taken = new Set((instructors || []).map((i) => i.userId).filter(Boolean));
        return (users || []).filter((u) =>
            u.role === 'Enseignant'
            && (u.id === ownUserId || (u.status === 'Active' && !taken.has(u.id))));
    }

    /** Corps de POST : jamais de statut (un Oustaz naît actif), ni de jeton. */
    function createBody(form) {
        return {
            fullName: (form.fullName || '').trim(),
            fullNameAr: blankToNull(form.fullNameAr),
            phone: blankToNull(form.phone),
            userId: form.userId || null
        };
    }

    /** Corps de PUT : la fiche ENTIÈRE (userId null détache le compte) et le jeton lu avec elle. */
    function updateBody(form) {
        return { ...createBody(form), status: form.status, rowVersion: form.rowVersion };
    }

    /** Corps d'un changement de statut seul : la fiche telle qu'elle est, avec le nouveau statut. */
    function statusChangeBody(instructor, status) {
        return {
            fullName: instructor.fullName,
            fullNameAr: instructor.fullNameAr ?? null,
            phone: instructor.phone ?? null,
            userId: instructor.userId ?? null,
            status,
            rowVersion: instructor.rowVersion
        };
    }

    // --- Affectation des élèves à une Halqa ---------------------------------------------------------

    /** Borne d'un envoi : la même que le serveur (HizbRules.MaxStudentsPerAssignment). Le serveur reste la barrière. */
    const MAX_ASSIGNMENT = 200;
    const CANDIDATE_PAGE_SIZE = 20;

    /** URL de la liste des élèves affectables : périmètre (`unassigned` | `all`), page, et recherche nettoyée si elle existe. */
    function assignableUrl({ scope, search, page, pageSize }) {
        const term = (search || '').trim();
        let url = `/internat/assignable-students?scope=${scope}&page=${page}&pageSize=${pageSize}`;
        if (term !== '') url += '&search=' + encodeURIComponent(term);
        return url;
    }

    /** Corps de POST assign-instructor pour AFFECTER des élèves à un Oustaz. */
    function assignBody(instructorId, studentIds) {
        return { instructorId, studentIds: [...studentIds] };
    }

    /** Corps de POST assign-instructor pour RETIRER un élève : `instructorId` null détache, l'élève n'est jamais supprimé. */
    function detachBody(studentId) {
        return { instructorId: null, studentIds: [studentId] };
    }

    /** Avertissement quand affecter un élève le retire de la Halqa d'un AUTRE Oustaz ; `null` s'il n'a pas de Halqa ou est déjà ici. */
    function moveWarning(student, instructorId) {
        if (!student.instructorId || student.instructorId === instructorId) return null;
        return 'Actuellement chez ' + (student.instructorName || 'un autre Oustaz') + ' : sera déplacé.';
    }

    function pageCount(total, pageSize) {
        return Math.max(1, Math.ceil((total || 0) / pageSize));
    }

    window.instructorsLogic = {
        STATUS_LABELS, MAX_ASSIGNMENT, statusLabel, filterInstructors, eligibleAccounts, createBody, updateBody, statusChangeBody,
        assignableUrl, assignBody, detachBody, moveWarning, pageCount
    };

    const EMPTY_FORM = () => ({
        open: false, mode: 'create', id: null, rowVersion: null, originalUserId: null,
        fullName: '', fullNameAr: '', phone: '', userId: '', status: 'Active',
        errors: {}, saving: false
    });

    const EMPTY_CONFIRM = () => ({ open: false, instructor: null, target: null, saving: false, error: null });

    /**
     * Fenêtre « Élèves » d'un Oustaz : sa Halqa actuelle, et les élèves que l'on peut lui affecter. `selected` garde les
     * identifiants cochés d'une recherche à l'autre (un objet, pour rester réactif sous Alpine).
     */
    const EMPTY_HALQA = () => ({
        open: false, instructor: null,
        members: [], membersLoading: false,
        scope: 'unassigned', search: '', page: 1, total: 0,
        candidates: [], candidatesLoading: false,
        selected: {}, pendingRemoval: null,
        saving: false, error: null, notice: null
    });

    document.addEventListener('alpine:init', () => {
        Alpine.data('instructorsPage', () => ({
            loading: true,
            error: null,
            instructors: [],
            users: [],           // comptes de l'école (Directeur seulement) pour le sélecteur de liaison
            search: '',
            statusFilter: '',
            canWrite: window.auth.role === 'Directeur',

            form: EMPTY_FORM(),
            confirm: EMPTY_CONFIRM(),
            halqa: EMPTY_HALQA(),
            membersError: null,      // erreurs de chargement de la fenêtre « Élèves » (portées par le composant, comme partout)
            candidatesError: null,

            statusLabel,

            async init() {
                await this.loadInstructors();
                if (this.canWrite) await this.loadAccounts();
            },

            /** `silent` : relire sans remplacer la liste par son squelette (rafraîchir les effectifs derrière une fenêtre ouverte). */
            async loadInstructors(silent = false) {
                if (!silent) this.loading = true;
                this.error = null;
                try {
                    this.instructors = await window.api.get('/internat/instructors');
                } catch (err) {
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement des Oustaz.');
                } finally {
                    this.loading = false;
                }
            },

            async loadAccounts() {
                try {
                    this.users = await window.api.get('/users');
                } catch (err) {
                    // silence-volontaire : GET /users est réservé au Directeur. Sans la liste, le sélecteur de compte est
                    // simplement vide ; la fiche reste créable et modifiable sans compte.
                    this.users = [];
                }
            },

            get filtered() {
                return filterInstructors(this.instructors, this.search, this.statusFilter);
            },

            get activeCount() {
                return this.instructors.filter((i) => i.status === 'Active').length;
            },

            get totalStudents() {
                return this.instructors.reduce((sum, i) => sum + (i.studentCount || 0), 0);
            },

            get accountOptions() {
                const options = eligibleAccounts(this.users, this.instructors, this.form.originalUserId)
                    .map((u) => ({ value: u.id, label: u.fullName + ' (' + u.email + ')' }));
                return [{ value: '', label: 'Aucun' }].concat(options);
            },

            // --- Création / modification -------------------------------------------------------------

            openCreate() {
                this.form = { ...EMPTY_FORM(), open: true, mode: 'create' };
            },

            openEdit(instructor) {
                this.form = {
                    ...EMPTY_FORM(), open: true, mode: 'edit',
                    id: instructor.id, rowVersion: instructor.rowVersion,
                    originalUserId: instructor.userId || null,
                    fullName: instructor.fullName, fullNameAr: instructor.fullNameAr || '',
                    phone: instructor.phone || '', userId: instructor.userId || '', status: instructor.status
                };
            },

            closeForm() {
                this.form = EMPTY_FORM();
            },

            async submitForm() {
                const f = this.form;
                if (f.saving) return;
                f.saving = true;
                f.errors = {};
                try {
                    if (f.mode === 'create') {
                        await window.api.post('/internat/instructors', createBody(f));
                    } else {
                        await window.api.put(`/internat/instructors/${f.id}`, updateBody(f));
                    }
                    this.closeForm();
                    await this.loadInstructors();
                    await this.loadAccounts();
                } catch (err) {
                    f.errors = window.api.toFieldErrors(err, 'Erreur lors de l\'enregistrement de l\'Oustaz.');
                    f.saving = false;
                    // Conflit d'écriture : on relit la liste pour que la réouverture de la fiche porte la version à jour.
                    if (err && err.status === 409) await this.loadInstructors();
                }
            },

            // --- Élèves de la Halqa : affecter, retirer ----------------------------------------------
            // Réservé au Directeur (le serveur le garde aussi : POST assign-instructor et GET assignable-students lui sont réservés).

            get canAssign() {
                return this.canWrite && !!this.halqa.instructor && this.halqa.instructor.status === 'Active';
            },

            get selectedCount() {
                return Object.keys(this.halqa.selected).length;
            },

            get pageCount() {
                return pageCount(this.halqa.total, CANDIDATE_PAGE_SIZE);
            },

            moveWarning(student) {
                return this.halqa.instructor ? moveWarning(student, this.halqa.instructor.id) : null;
            },

            async openHalqa(instructor) {
                if (!this.canWrite) return;
                this.halqa = { ...EMPTY_HALQA(), open: true, instructor };
                this.membersError = null;
                this.candidatesError = null;
                await Promise.all([this.loadMembers(), this.loadCandidates()]);
            },

            /** Ferme la fenêtre et relit la liste : les effectifs affichés ont pu changer pendant l'affectation. */
            async closeHalqa() {
                this.halqa = EMPTY_HALQA();
                this.membersError = null;
                this.candidatesError = null;
                await this.loadInstructors(true);
            },

            async loadMembers() {
                const h = this.halqa;
                h.membersLoading = true;
                this.membersError = null;
                try {
                    const result = await window.api.get(`/internat/instructors/${h.instructor.id}/students`);
                    h.members = result.students;
                } catch (err) {
                    this.membersError = window.api.toMessage(err, 'Erreur lors du chargement des élèves de la Halqa.');
                } finally {
                    h.membersLoading = false;
                }
            },

            async loadCandidates() {
                const h = this.halqa;
                h.candidatesLoading = true;
                this.candidatesError = null;
                try {
                    const result = await window.api.get(assignableUrl({
                        scope: h.scope, search: h.search, page: h.page, pageSize: CANDIDATE_PAGE_SIZE
                    }));
                    h.candidates = result.items;
                    h.total = result.totalCount;
                } catch (err) {
                    h.candidates = [];
                    h.total = 0;
                    this.candidatesError = window.api.toMessage(err, 'Erreur lors du chargement des élèves.');
                } finally {
                    h.candidatesLoading = false;
                }
            },

            /** Nouvelle recherche ou nouveau périmètre : retour à la première page, la sélection est conservée. */
            async searchCandidates() {
                this.halqa.page = 1;
                await this.loadCandidates();
            },

            async goToCandidatePage(target) {
                if (target < 1 || target > this.pageCount) return;
                this.halqa.page = target;
                await this.loadCandidates();
            },

            /** Un élève déjà dans CETTE Halqa n'a rien à se voir affecter ; un élève d'une autre Halqa est déplaçable (avertissement). */
            isSelectable(student) {
                return !!this.halqa.instructor && student.instructorId !== this.halqa.instructor.id;
            },

            isSelected(studentId) {
                return !!this.halqa.selected[studentId];
            },

            toggleSelect(student) {
                if (!this.isSelectable(student)) return;
                if (this.halqa.selected[student.studentId]) {
                    delete this.halqa.selected[student.studentId];
                } else {
                    this.halqa.selected[student.studentId] = true;
                }
            },

            /** Vrai quand tous les élèves AFFECTABLES de la page sont cochés (état de la case « tout sélectionner »). */
            get pageFullySelected() {
                const selectable = this.halqa.candidates.filter((c) => this.isSelectable(c));
                return selectable.length > 0 && selectable.every((c) => this.isSelected(c.studentId));
            },

            /** Coche (ou décoche, si tout l'est déjà) les élèves affectables de la page affichée. */
            toggleSelectPage() {
                const allSelected = this.pageFullySelected;
                this.halqa.candidates.filter((c) => this.isSelectable(c)).forEach((c) => {
                    if (allSelected) delete this.halqa.selected[c.studentId];
                    else this.halqa.selected[c.studentId] = true;
                });
            },

            /** Relit tout ce que l'écriture a pu changer : la Halqa, la liste des candidats et, derrière la fenêtre, les effectifs. */
            async refreshAfterWrite() {
                await Promise.all([this.loadMembers(), this.loadCandidates(), this.loadInstructors(true)]);
            },

            async assignSelected() {
                const h = this.halqa;
                const count = this.selectedCount;
                if (h.saving || !this.canAssign || count === 0) return;
                h.notice = null;
                h.error = null;
                if (count > MAX_ASSIGNMENT) {
                    h.error = `Une affectation porte sur ${MAX_ASSIGNMENT} élèves au maximum : décochez-en ${count - MAX_ASSIGNMENT}.`;
                    return;
                }
                h.saving = true;
                try {
                    await window.api.post('/internat/students/assign-instructor', assignBody(h.instructor.id, Object.keys(h.selected)));
                    h.selected = {};
                    h.notice = `${count} ${count === 1 ? 'élève affecté' : 'élèves affectés'} à ${h.instructor.fullName}.`;
                    await this.refreshAfterWrite();
                } catch (err) {
                    h.error = window.api.toMessage(err, "Erreur lors de l'affectation des élèves.");
                } finally {
                    h.saving = false;
                }
            },

            askRemove(student) {
                this.halqa.pendingRemoval = student.studentId;
                this.halqa.notice = null;
                this.halqa.error = null;
            },

            cancelRemove() {
                this.halqa.pendingRemoval = null;
            },

            /** Retirer = détacher de la Halqa. Le suivi Hizb de l'élève est conservé : il le suivra dans sa prochaine Halqa. */
            async confirmRemove() {
                const h = this.halqa;
                const student = h.members.find((m) => m.studentId === h.pendingRemoval);
                if (h.saving || !student || !this.canWrite) return;
                h.saving = true;
                h.error = null;
                try {
                    await window.api.post('/internat/students/assign-instructor', detachBody(student.studentId));
                    h.pendingRemoval = null;
                    h.notice = `${student.fullName} n'est plus dans cette Halqa.`;
                    await this.refreshAfterWrite();
                } catch (err) {
                    h.error = window.api.toMessage(err, "Erreur lors du retrait de l'élève.");
                } finally {
                    h.saving = false;
                }
            },

            // --- Suspension / réactivation -----------------------------------------------------------

            openStatusChange(instructor, target) {
                this.confirm = { open: true, instructor, target, saving: false, error: null };
            },

            closeConfirm() {
                this.confirm = EMPTY_CONFIRM();
            },

            async confirmStatusChange() {
                const c = this.confirm;
                if (c.saving || !c.instructor) return;
                c.saving = true;
                c.error = null;
                try {
                    await window.api.put(`/internat/instructors/${c.instructor.id}`, statusChangeBody(c.instructor, c.target));
                    this.closeConfirm();
                    await this.loadInstructors();
                } catch (err) {
                    c.error = window.api.toMessage(err, 'Erreur lors du changement de statut.');
                    c.saving = false;
                    if (err && err.status === 409) await this.loadInstructors();
                }
            }
        }));
    });
})();
