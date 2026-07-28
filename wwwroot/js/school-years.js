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

        // Confirmation « Année scolaire ajoutée » affichée après un enregistrement réussi.
        showAddedDialog: false,
        addedYearLabel: '',

        // --- Modification du libellé / de la période (PUT /school-years/{id}) ---
        //
        // Un drapeau d'ouverture SÉPARÉ de la copie de travail, comme pour la création : modal-shell
        // garde son contenu dans le DOM, et un x-model sur un objet nul planterait à l'évaluation.
        isEditOpen: false,
        editingYear: { id: null, label: '', startDate: '', endDate: '' },
        isSavingEdit: false,
        editErrors: {},
        showEditedDialog: false,
        editedYearLabel: '',

        // --- Activation (double confirmation par mot de passe, Volume_7_Security §16) ---
        yearToActivate: null,
        password: '',
        isActivating: false,
        activateErrors: {},
        showActivatedDialog: false,
        activatedYearLabel: '',

        // --- Export (ZIP élèves/paiements/classes) ---
        exportingYearId: null,

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
                active: 'status-badge-success',
                closed: 'status-badge-neutral',
                upcoming: 'status-badge-primary',
                inactive: 'status-badge-warning'
            }[this.statusOf(year)];
        },

        /** Une année terminée est en LECTURE SEULE : elle ne peut plus redevenir l'exercice courant. */
        canActivate(year) {
            return this.isDirecteur && !year.isActive && !year.isClosed;
        },

        /**
         * Modifier reste possible sur l'année ACTIVE (prolonger l'exercice en cours est le cas d'usage
         * principal) comme sur une année à venir, mais jamais sur une année terminée : les frais et les
         * bulletins d'un exercice clos sont arrêtés. Même règle côté serveur, qui refuse en 422 —
         * masquer le bouton n'est qu'un confort d'affichage.
         */
        canEdit(year) {
            return this.isDirecteur && !year.isClosed;
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
        overlapsExistingYear(candidate = this.newYear, excludeId = null) {
            const { startDate, endDate } = candidate;
            if (!startDate || !endDate) return null;

            // excludeId : une année en cours de MODIFICATION ne chevauche pas sa propre période.
            return this.years.find((year) =>
                year.id !== excludeId && year.startDate <= endDate && startDate <= year.endDate) || null;
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
                this.addedYearLabel = this.newYear.label;
                await this.loadYears();
                this.showAddedDialog = true; // confirmation « Année scolaire ajoutée »
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, "Erreur lors de la création de l'année scolaire.");
            } finally {
                this.isSubmitting = false;
            }
        },

        // --------------------------------------------- Modification (libellé / période)

        openEdit(year) {
            // Copie de TRAVAIL, jamais la ligne du tableau : l'éditer en direct ferait bouger
            // l'affichage pendant la saisie, et laisserait des valeurs fausses à l'écran si l'API refuse.
            this.editingYear = {
                id: year.id,
                label: year.label,
                startDate: year.startDate,
                endDate: year.endDate
            };
            this.editErrors = {};
            this.isEditOpen = true;
        },

        closeEdit() {
            this.isEditOpen = false;
            this.editErrors = {};
        },

        async submitEdit() {
            if (!this.editingYear.id) return;

            this.editErrors = {};

            // Même pré-contrôle que la création, en s'excluant soi-même. La règle reste celle du serveur
            // (UpdateSchoolYearCommandHandler, 422) : on évite seulement un aller-retour inutile.
            const overlap = this.overlapsExistingYear(this.editingYear, this.editingYear.id);
            if (overlap) {
                this.editErrors = {
                    startdate: `Cette période chevauche l'année scolaire « ${overlap.label} » déjà enregistrée.`
                };
                return;
            }

            this.isSavingEdit = true;
            try {
                await window.api.put(`/school-years/${this.editingYear.id}`, {
                    label: this.editingYear.label,
                    startDate: this.editingYear.startDate,
                    endDate: this.editingYear.endDate
                });

                this.editedYearLabel = this.editingYear.label;
                this.closeEdit();

                // Rechargement complet : le serveur a pu recaler les trimestres, et c'est l'état RÉEL
                // de l'année qu'il faut afficher — pas ce que le navigateur croit avoir envoyé.
                await this.loadYears();
                this.showEditedDialog = true;
            } catch (err) {
                this.editErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
            } finally {
                this.isSavingEdit = false;
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

                this.activatedYearLabel = this.yearToActivate.label;
                this.closeActivate();

                // Rechargement complet, et non mise à jour locale des deux lignes concernées :
                // l'ancienne année active est retombée côté serveur, et c'est son état RÉEL qu'il faut
                // afficher — pas ce que le navigateur croit qu'il s'est passé.
                await this.loadYears();
                this.showActivatedDialog = true;
            } catch (err) {
                this.activateErrors = window.api.toFieldErrors(err, "Erreur lors de l'activation.");
                this.password = '';
            } finally {
                this.isActivating = false;
            }
        },

        // ------------------------------------------------------------------ Export

        /**
         * Télécharge le ZIP (élèves, paiements, classes) de l'année — même patron que
         * dashboard.js/enrollments.js : l'API exige le jeton, on récupère donc le fichier en blob avec
         * l'en-tête Authorization plutôt qu'un simple lien.
         */
        async exportYear(year) {
            if (!year || !year.id || year.id === 'undefined') {
                console.error('Identifiant d\'année scolaire invalide ou indéfini', year && year.id);
                return;
            }
            this.exportingYearId = year.id;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(`/api/v1/school-years/${year.id}/export`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) {
                    this.error = "Erreur lors de l'export de l'année scolaire.";
                    return;
                }

                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = `Export-${year.label}.zip`;
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } finally {
                this.exportingYearId = null;
            }
        }
    }));
});
