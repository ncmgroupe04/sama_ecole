document.addEventListener('alpine:init', () => {
    // Ordre pédagogique des cycles ; un niveau hors nomenclature passe en fin, par ordre alphabétique.
    const LEVEL_ORDER = ['Crèche', 'Maternelle', 'Primaire', 'Collège', 'Lycée'];

    // Les 5 cycles affichés en colonnes fixes de la grille de l'écran Classes, gauche → droite.
    // Coïncide avec LEVEL_ORDER : tout niveau hors de cette liste (donnée héritée, future
    // nomenclature) reste géré par otherGroups plutôt que silencieusement perdu.
    const MAIN_CYCLES = ['Crèche', 'Maternelle', 'Primaire', 'Collège', 'Lycée'];

    // Succession pédagogique RÉELLE (CE1 < CI alphabétiquement serait faux) : chaque classe est
    // rattachée au premier jeton qu'elle commence par (ex. « CE1 B » → CE1, « Terminale S1 » →
    // Terminale) ; une classe hors nomenclature (ex. « Test Classe Verif ») part en fin de cycle.
    // Un rang peut lister plusieurs jetons alternatifs (ex. Maternelle : sigle ou nom complet) —
    // le premier qui correspond au début du nom de la classe détermine son rang.
    const GRADE_ORDER = {
        'Maternelle': [['TPS', 'Toute Petite Section'], ['PS', 'Petite Section'], ['MS', 'Moyenne Section'], ['GS', 'Grande Section']],
        'Primaire': ['CI', 'CP', 'CE1', 'CE2', 'CM1', 'CM2'],
        'Collège': [['6e', 'Sixième'], ['5e', 'Cinquième'], ['4e', 'Quatrième'], ['3e', 'Troisième']],
        'Lycée': [['Seconde', '2nde', '2nd'], ['Première', '1ère', '1re'], ['Terminale', 'Tle']]
    };

    const escapeRegex = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

    // Abrégés D'AFFICHAGE seulement (grille compacte) : « Sixième A » → « 6ème A », « Seconde S »
    // → « 2nde S », « Terminale S1 » → « Tle S1 ». Le nom réel (recherche, édition, suppression,
    // API) reste inchangé — seule cette étiquette est raccourcie ; le nom complet reste lisible via
    // l'attribut title (survol) posé sur la même ligne.
    const CLASS_ABBREVIATIONS = [
        [/^Sixième(?=\s|$)/i, '6ème'],
        [/^Cinquième(?=\s|$)/i, '5ème'],
        [/^Quatrième(?=\s|$)/i, '4ème'],
        [/^Troisième(?=\s|$)/i, '3ème'],
        [/^Seconde(?=\s|$)/i, '2nde'],
        [/^Première(?=\s|$)/i, '1ère'],
        [/^Terminale(?=\s|$)/i, 'Tle']
    ];

    function displayName(name) {
        const match = CLASS_ABBREVIATIONS.find(([re]) => re.test(name));
        return match ? name.replace(match[0], match[1]) : name;
    }

    function gradeRank(level, name) {
        const tokens = GRADE_ORDER[level];
        if (!tokens) return -1;
        const trimmed = name.trim();
        return tokens.findIndex((entry) => (Array.isArray(entry) ? entry : [entry])
            .some((token) => new RegExp(`^${escapeRegex(token)}(\\s|$)`, 'i').test(trimmed)));
    }

    function byGradeThenName(level) {
        return (a, b) => {
            const ra = gradeRank(level, a.name);
            const rb = gradeRank(level, b.name);
            const tokenCount = (GRADE_ORDER[level] || []).length;
            if (ra !== rb) return (ra === -1 ? tokenCount : ra) - (rb === -1 ? tokenCount : rb);
            // « numeric: true » pour que 6e < 10e — un tri texte brut mettrait 10e avant 6e.
            return a.name.localeCompare(b.name, 'fr', { numeric: true });
        };
    }

    Alpine.data('classroomsView', () => ({
        classrooms: [],
        isLoading: false,
        error: null,

        // Recherche + filtre niveau (Volume 5 §6 : recherche et filtres sur toutes les listes)
        search: '',
        levelFilter: '',

        // Modal State
        isCreateOpen: false,
        isSubmitting: false,
        newClassroom: {
            name: '',
            level: 'Primaire',
            capacity: 30
        },
        createErrors: {},

        // Confirmation « Classe ajoutée » affichée après un enregistrement réussi.
        showAddedDialog: false,
        addedClassroomName: '',

        // Corriger/archiver une classe déjà créée : réservé au Directeur et au Secrétariat côté
        // serveur (ClassroomsController.ManageRoles) — ce booléen n'est qu'un confort d'affichage.
        canManage: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // Édition (modale)
        editing: null, // { id, name, level, capacity, rowVersion }
        isSavingEdit: false,
        editErrors: {},
        showEditedDialog: false,
        editedClassroomName: '',

        // Suppression (modale de confirmation)
        deletingClassroom: null, // { id, name, rowVersion }
        isDeletingClassroom: false,
        deleteClassroomError: null,
        showDeletedDialog: false,
        deletedClassroomName: '',

        init() {
            this.loadClassrooms();
        },

        /**
         * Une section par cycle (même lecture que l'écran Matières) : le niveau se voit au premier
         * regard dans l'en-tête de section, plus besoin de le répéter devant chaque classe.
         */
        get groups() {
            const q = this.search.trim().toLowerCase();
            const visible = this.classrooms.filter((c) =>
                (!q || c.name.toLowerCase().includes(q) || c.level.toLowerCase().includes(q)) &&
                (!this.levelFilter || c.level === this.levelFilter));

            const byLevel = new Map();
            for (const classroom of visible) {
                if (!byLevel.has(classroom.level)) byLevel.set(classroom.level, []);
                byLevel.get(classroom.level).push(classroom);
            }

            return Array.from(byLevel, ([level, classrooms]) => ({
                level,
                classrooms: [...classrooms].sort(byGradeThenName(level)),
                totalCapacity: classrooms.reduce((sum, c) => sum + (c.capacity || 0), 0),
                totalStudents: classrooms.reduce((sum, c) => sum + (c.studentCount || 0), 0)
            })).sort((a, b) => {
                const ia = LEVEL_ORDER.indexOf(a.level);
                const ib = LEVEL_ORDER.indexOf(b.level);
                if (ia !== ib) return (ia === -1 ? LEVEL_ORDER.length : ia) - (ib === -1 ? LEVEL_ORDER.length : ib);
                return a.level.localeCompare(b.level, 'fr');
            });
        },

        /**
         * Grille principale de l'écran : toujours 5 colonnes, une par cycle (Crèche/Maternelle/
         * Primaire/Collège/Lycée), même quand un cycle n'a encore aucune classe — la colonne reste
         * visible avec son en-tête, plutôt que de faire sauter la mise en page selon les données
         * du moment.
         */
        get mainGroups() {
            return MAIN_CYCLES.map((level) =>
                this.groups.find((g) => g.level === level) ||
                { level, classrooms: [], totalCapacity: 0, totalStudents: 0 });
        },

        /** Filet de sécurité : un niveau hors des 5 colonnes fixes (donnée héritée, future
         *  nomenclature) reste affiché à part plutôt que silencieusement perdu. En pratique vide,
         *  MAIN_CYCLES couvrant déjà toute la liste déroulante Niveau du formulaire. */
        get otherGroups() {
            return this.groups.filter((g) => !MAIN_CYCLES.includes(g.level));
        },

        async loadClassrooms() {
            this.isLoading = true;
            this.error = null;
            try {
                // The API endpoint /classrooms returns a list of classrooms
                const data = await window.api.get('/classrooms');
                // Assume the response is either an array or has an items property
                this.classrooms = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                this.error = err.message || "Erreur lors du chargement des classes.";
            } finally {
                this.isLoading = false;
            }
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                await window.api.post('/classrooms', this.newClassroom);

                this.isCreateOpen = false;
                this.addedClassroomName = this.newClassroom.name;
                this.newClassroom = { name: '', level: 'Primaire', capacity: 30 };
                await this.loadClassrooms();
                this.showAddedDialog = true; // confirmation « Classe ajoutée »
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, "Erreur lors de la création.");
            } finally {
                this.isSubmitting = false;
            }
        },

        // ------------------------------------------------------------ Édition

        openEdit(classroom) {
            this.editing = {
                id: classroom.id,
                name: classroom.name,
                level: classroom.level,
                capacity: classroom.capacity,
                rowVersion: classroom.rowVersion
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
                await window.api.put(`/classrooms/${this.editing.id}`, {
                    name: this.editing.name,
                    level: this.editing.level,
                    capacity: this.editing.capacity,
                    rowVersion: this.editing.rowVersion
                });
                this.editedClassroomName = this.editing.name;
                this.closeEdit();
                await this.loadClassrooms();
                this.showEditedDialog = true;
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    // Verrou optimiste (AGENTS.md règle #5) : jamais un écrasement silencieux — on
                    // recharge pour montrer l'état réel avant de laisser l'utilisateur réessayer.
                    this.editErrors = { global: 'Cette classe vient d\'être modifiée par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez.' };
                    await this.loadClassrooms();
                } else {
                    this.editErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEdit = false;
            }
        },

        // ------------------------------------------------------------ Suppression

        openDelete(classroom) {
            this.deletingClassroom = { id: classroom.id, name: classroom.name, rowVersion: classroom.rowVersion };
            this.deleteClassroomError = null;
        },

        closeDelete() {
            this.deletingClassroom = null;
            this.deleteClassroomError = null;
        },

        async confirmDelete() {
            if (!this.deletingClassroom) return;

            this.isDeletingClassroom = true;
            this.deleteClassroomError = null;
            try {
                await window.api.delete(`/classrooms/${this.deletingClassroom.id}?rowVersion=${this.deletingClassroom.rowVersion}`);
                this.deletedClassroomName = this.deletingClassroom.name;
                this.deletingClassroom = null;
                await this.loadClassrooms();
                this.showDeletedDialog = true;
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    // Des élèves sont encore rattachés à cette classe (DeleteClassroomCommandHandler) :
                    // le message serveur est déjà explicite, on l'affiche tel quel.
                    this.deleteClassroomError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteClassroomError = 'Cette classe vient d\'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.';
                    await this.loadClassrooms();
                } else {
                    this.deleteClassroomError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingClassroom = false;
            }
        },

        // Utilities
        getTotalCapacity() {
            return this.classrooms.reduce((sum, c) => sum + (c.capacity || 0), 0);
        },

        plural(count, singular, plural) {
            return count + ' ' + (count > 1 ? plural : singular);
        },

        displayName(name) {
            return displayName(name);
        }
    }));
});
