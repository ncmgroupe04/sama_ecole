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

    window.instructorsLogic = {
        STATUS_LABELS, statusLabel, filterInstructors, eligibleAccounts, createBody, updateBody, statusChangeBody
    };

    const EMPTY_FORM = () => ({
        open: false, mode: 'create', id: null, rowVersion: null, originalUserId: null,
        fullName: '', fullNameAr: '', phone: '', userId: '', status: 'Active',
        errors: {}, saving: false
    });

    const EMPTY_CONFIRM = () => ({ open: false, instructor: null, target: null, saving: false, error: null });

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

            statusLabel,

            async init() {
                await this.loadInstructors();
                if (this.canWrite) await this.loadAccounts();
            },

            async loadInstructors() {
                this.loading = true;
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
