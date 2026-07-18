/**
 * JGK-D03/D04 — Enseignants. Liste paginée + recherche (GET /teachers), création de fiche
 * (POST /teachers, matricule généré côté serveur), fiche détaillée (GET /teachers/{id}) avec matières
 * qualifiées et historique des affectations, et attribution classe/matière (POST /teachers/{id}/assignments).
 *
 * Permissions (confort d'affichage ; l'API garde reste la protection réelle) : Voir → Super Admin /
 * Directeur / Secrétariat ; Créer & Attribuer → Directeur / Secrétariat.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('teachersView', () => ({
        teachers: [],
        subjects: [],
        eligibleAccounts: [],
        totalCount: 0,
        page: 1,
        pageSize: 10,
        isLoading: false,
        error: null,
        search: '',

        canManage: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',
        // Seul le Directeur peut lister les comptes (GET /users) : le champ de rattachement ne s'affiche
        // donc que pour lui. Le Secrétariat crée la fiche sans lien ; le Directeur le posera plus tard.
        canLinkAccount: window.auth.role === 'Directeur',

        // Slide-over création
        isCreateOpen: false,
        isSubmitting: false,
        newTeacher: { fullName: '', email: '', phone: '', subjectIds: [], userId: '' },
        createErrors: {},

        // Slide-over détail
        detail: null,
        detailLoading: false,
        assignForm: { classroomId: '', subjectId: '' },
        assignError: null,
        assignSubmitting: false,
        classrooms: [],

        init() {
            this.loadSubjects();
            this.loadTeachers();
            if (this.canLinkAccount) this.loadEligibleAccounts();
            if (this.canManage) this.loadClassrooms();
        },

        async loadSubjects() {
            try {
                const data = await window.api.get('/subjects');
                this.subjects = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement matières:', err);
            }
        },

        async loadClassrooms() {
            try {
                const data = await window.api.get('/classrooms');
                this.classrooms = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement classes:', err);
            }
        },

        /** Comptes rattachables : rôle Enseignant, non déjà liés (le serveur refuse un doublon de toute façon). */
        async loadEligibleAccounts() {
            try {
                const users = await window.api.get('/users');
                this.eligibleAccounts = (users || []).filter((u) => u.role === 'Enseignant');
            } catch (err) {
                console.error('Erreur chargement comptes:', err);
            }
        },

        async loadTeachers() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                if (this.search.trim()) params.set('search', this.search.trim());

                const data = await window.api.get(`/teachers?${params.toString()}`);
                this.teachers = data.items || [];
                this.totalCount = data.totalCount || 0;
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des enseignants.';
            } finally {
                this.isLoading = false;
            }
        },

        applyFilters() {
            this.page = 1;
            this.loadTeachers();
        },

        resetFilters() {
            this.search = '';
            this.applyFilters();
        },

        openCreate() {
            this.newTeacher = { fullName: '', email: '', phone: '', subjectIds: [], userId: '' };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                const payload = {
                    fullName: this.newTeacher.fullName,
                    email: this.newTeacher.email,
                    phone: this.newTeacher.phone || null,
                    subjectIds: this.newTeacher.subjectIds,
                    userId: this.newTeacher.userId || null
                };
                await window.api.post('/teachers', payload);

                this.isCreateOpen = false;
                this.page = 1;
                await this.loadTeachers();
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, 'Une erreur est survenue lors de la création.');
            } finally {
                this.isSubmitting = false;
            }
        },

        async openDetail(teacher) {
            this.detail = null;
            this.assignForm = { classroomId: '', subjectId: '' };
            this.assignError = null;
            this.detailLoading = true;
            try {
                this.detail = await window.api.get(`/teachers/${teacher.id}`);
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement de la fiche.';
            } finally {
                this.detailLoading = false;
            }
        },

        closeDetail() {
            this.detail = null;
        },

        /** Affectations regroupées par année scolaire (l'historique), année active en tête. */
        assignmentsByYear() {
            if (!this.detail || !this.detail.assignments) return [];
            const groups = {};
            for (const a of this.detail.assignments) {
                if (!groups[a.schoolYearId]) {
                    groups[a.schoolYearId] = { label: a.schoolYearLabel, isActive: a.isActiveSchoolYear, items: [] };
                }
                groups[a.schoolYearId].items.push(a);
            }
            return Object.values(groups).sort((x, y) => (y.isActive ? 1 : 0) - (x.isActive ? 1 : 0));
        },

        async submitAssign() {
            this.assignSubmitting = true;
            this.assignError = null;
            try {
                await window.api.post(`/teachers/${this.detail.id}/assignments`, {
                    classroomId: this.assignForm.classroomId,
                    subjectId: this.assignForm.subjectId
                });
                // Recharge la fiche pour refléter la nouvelle affectation.
                const id = this.detail.id;
                this.assignForm = { classroomId: '', subjectId: '' };
                this.detail = await window.api.get(`/teachers/${id}`);
            } catch (err) {
                this.assignError = err.message || "Erreur lors de l'attribution.";
            } finally {
                this.assignSubmitting = false;
            }
        },

        statusLabel(status) {
            const labels = { Active: 'Actif', Suspended: 'Suspendu', Blocked: 'Bloqué' };
            return labels[status] || status;
        },

        statusClass(status) {
            if (status === 'Active') return 'bg-success-bg text-success';
            if (status === 'Suspended') return 'bg-warning-bg text-warning';
            return 'bg-danger-bg text-danger';
        },

        initials(name) {
            return (name || '').split(' ').filter(Boolean).slice(0, 2).map((p) => p[0]).join('').toUpperCase();
        }
    }));
});
