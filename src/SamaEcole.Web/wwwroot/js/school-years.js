/**
 * Écran Années scolaires — ticket JGK-C01.
 *
 * Rien ici ne DÉCIDE quoi que ce soit : le serveur reste seul juge. Le rôle est relu du JWT pour
 * masquer des boutons inutiles, le chevauchement est signalé avant l'envoi pour éviter un
 * aller-retour sur une connexion mobile lente — mais l'API refuse dans tous les cas, et c'est elle
 * qui fait foi (voir SchoolYearsController et ses tests fonctionnels).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('schoolYearsView', () => ({
        years: [],
        isLoading: false,
        error: null,

        /** Le Sénégal est à UTC+0 toute l'année : la date UTC est la date locale, sans décalage. */
        today: new Date().toISOString().slice(0, 10),

        /**
         * Confort d'affichage uniquement. Un utilisateur qui forcerait ce booléen à true ne gagnerait
         * rien : le contrôleur est [Authorize(Roles = Directeur)] et répondrait 403.
         */
        isDirecteur: window.auth.role === 'Directeur',

        // --- Création ---
        isCreateOpen: false,
        isSubmitting: false,
        newYear: { label: '', startDate: '', endDate: '' },
        createErrors: {},

        // --- Activation (double confirmation par mot de passe, Volume_7_Security §16) ---
        yearToActivate: null,
        password: '',
        isActivating: false,
        activateErrors: {},

        init() {
            this.loadYears();
        },

        async loadYears() {
            this.isLoading = true;
            this.error = null;
            try {
                this.years = await window.api.get('/school-years');
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des années scolaires.';
            } finally {
                this.isLoading = false;
            }
        },

        /**
         * Quatre états, et non trois. Une année peut être EN COURS sans être l'année active : c'est le
         * cas de l'ancienne année, le jour où le Directeur bascule sur la suivante avant la fin de la
         * précédente. La passer sous silence donnerait un tableau où une ligne n'a aucun badge.
         */
        statusOf(year) {
            if (year.isActive) return 'active';
            if (year.isClosed) return 'closed';
            if (year.startDate > this.today) return 'upcoming';
            return 'inactive';
        },

        statusLabel(year) {
            return {
                active: 'Active',
                closed: 'Terminée',
                upcoming: 'À venir',
                inactive: 'Inactive'
            }[this.statusOf(year)];
        },

        statusClass(year) {
            return {
                active: 'bg-green-50 text-success ring-1 ring-inset ring-green-600/20',
                closed: 'bg-gray-100 text-gray-600 ring-1 ring-inset ring-gray-500/20',
                upcoming: 'bg-indigo-50 text-primary ring-1 ring-inset ring-indigo-600/20',
                inactive: 'bg-orange-50 text-warning ring-1 ring-inset ring-orange-600/20'
            }[this.statusOf(year)];
        },

        /** Une année terminée est en LECTURE SEULE : elle ne peut plus redevenir l'exercice courant. */
        canActivate(year) {
            return this.isDirecteur && !year.isActive && !year.isClosed;
        },

        activeYear() {
            return this.years.find((year) => year.isActive) || null;
        },

        /**
         * Format d'affichage : jj/mm/aaaa, le défaut de l'établissement. Le réglage `dateFormat` de
         * JGK-B02 n'est pas encore branché sur l'interface — le jour où il le sera, c'est ici qu'il
         * entrera, et nulle part ailleurs.
         */
        formatDate(isoDate) {
            if (!isoDate) return '';

            const [year, month, day] = isoDate.split('-');
            return `${day}/${month}/${year}`;
        },

        // ---------------------------------------------------------------- Création

        openCreate() {
            this.newYear = { label: '', startDate: '', endDate: '' };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        /**
         * Pré-contrôle du chevauchement, côté navigateur. Ce n'est PAS la règle : la règle est dans
         * CreateSchoolYearCommandHandler, qui refuse en 422. C'est un raccourci d'affichage — la liste
         * est déjà chargée, autant répondre tout de suite plutôt que faire attendre un aller-retour à
         * une connexion mobile.
         */
        overlapsExistingYear() {
            const { startDate, endDate } = this.newYear;
            if (!startDate || !endDate) return null;

            return this.years.find((year) => year.startDate <= endDate && startDate <= year.endDate) || null;
        },

        async submitCreate() {
            this.createErrors = {};

            const overlap = this.overlapsExistingYear();
            if (overlap) {
                this.createErrors = {
                    startdate: `Cette période chevauche l'année scolaire « ${overlap.label} » déjà enregistrée.`
                };
                return;
            }

            this.isSubmitting = true;
            try {
                await window.api.post('/school-years', this.newYear);

                this.isCreateOpen = false;
                await this.loadYears();
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, "Erreur lors de la création de l'année scolaire.");
            } finally {
                this.isSubmitting = false;
            }
        },

        // ------------------------------------------------------- Activation (confirmée)

        openActivate(year) {
            this.yearToActivate = year;
            this.password = '';
            this.activateErrors = {};
        },

        closeActivate() {
            this.yearToActivate = null;
            this.password = ''; // ne jamais laisser traîner une saisie de mot de passe en mémoire
            this.activateErrors = {};
        },

        async submitActivate() {
            if (!this.yearToActivate) return;

            this.isActivating = true;
            this.activateErrors = {};

            try {
                await window.api.post(
                    `/school-years/${this.yearToActivate.id}/activate`,
                    { password: this.password });

                this.closeActivate();

                // Rechargement complet, et non mise à jour locale des deux lignes concernées :
                // l'ancienne année active est retombée côté serveur, et c'est son état RÉEL qu'il faut
                // afficher — pas ce que le navigateur croit qu'il s'est passé.
                await this.loadYears();
            } catch (err) {
                this.activateErrors = window.api.toFieldErrors(err, "Erreur lors de l'activation.");
                this.password = '';
            } finally {
                this.isActivating = false;
            }
        }
    }));
});
