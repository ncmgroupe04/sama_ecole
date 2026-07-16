/**
 * Écran Matières et coefficients — ticket JGK-C03.
 *
 * Le serveur reste seul juge : le rôle est relu du JWT pour masquer le bouton de création, mais
 * l'API répond 403 à qui l'appellerait quand même (SubjectsController est [Authorize(Roles =
 * Directeur)] en écriture).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('subjectsView', () => ({
        subjects: [],
        isLoading: false,
        error: null,
        search: '',

        isDirecteur: window.auth.role === 'Directeur',

        isCreateOpen: false,
        isSubmitting: false,
        newSubject: { name: '', level: '', coefficient: 1 },
        createErrors: {},

        init() {
            this.loadSubjects();
        },

        async loadSubjects() {
            this.isLoading = true;
            this.error = null;
            try {
                this.subjects = await window.api.get('/subjects');
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des matières.';
            } finally {
                this.isLoading = false;
            }
        },

        /**
         * Regroupement par niveau, calculé à la volée. La liste arrive déjà triée par niveau puis par
         * nom (GetSubjectsQueryHandler) : il suffit de la découper, sans re-trier ici.
         */
        get groups() {
            const q = this.search.trim().toLowerCase();
            const visible = q
                ? this.subjects.filter((s) => s.name.toLowerCase().includes(q) || s.level.toLowerCase().includes(q))
                : this.subjects;

            const byLevel = new Map();

            for (const subject of visible) {
                if (!byLevel.has(subject.level)) byLevel.set(subject.level, []);
                byLevel.get(subject.level).push(subject);
            }

            return Array.from(byLevel, ([level, subjects]) => ({
                level,
                subjects,
                totalCoefficient: subjects.reduce((sum, s) => sum + Number(s.coefficient), 0)
            }));
        },

        /**
         * Niveaux déjà utilisés, proposés en autocomplétion du champ « Niveau ». Confort de saisie
         * pour éviter qu'une même écriture — « Primaire » vs « primaire » — ne crée deux niveaux
         * distincts ; l'utilisateur reste libre d'en taper un nouveau.
         */
        get knownLevels() {
            return [...new Set(this.subjects.map((s) => s.level))].sort();
        },

        formatCoefficient(value) {
            // Affiche « 4 » et non « 4,00 », mais « 1,5 » reste « 1,5 ».
            return Number(value).toLocaleString('fr-FR');
        },

        openCreate() {
            // Reprend le dernier niveau saisi : on crée en général toutes les matières d'un niveau à
            // la suite. Repartir d'un champ vide à chaque fois ferait retaper « Primaire » dix fois.
            const lastLevel = this.subjects.length ? this.subjects[this.subjects.length - 1].level : '';
            this.newSubject = { name: '', level: lastLevel, coefficient: 1 };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                await window.api.post('/subjects', this.newSubject);

                this.isCreateOpen = false;
                await this.loadSubjects();
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la matière.');
            } finally {
                this.isSubmitting = false;
            }
        }
    }));
});
