document.addEventListener('alpine:init', () => {
    // Ordre d'affichage des cycles (voulu par l'école : Primaire/Collège/Lycée en avant, Crèche/
    // Maternelle en dernier) ; un niveau hors nomenclature passe en fin, par ordre alphabétique.
    const LEVEL_ORDER = ['Primaire', 'Collège', 'Lycée', 'Crèche', 'Maternelle'];

    // Les 5 cycles affichés dans la grille de l'écran Classes, dans l'ordre. Avec la grille sur
    // 3 colonnes, les 3 premiers (Primaire/Collège/Lycée) forment la 1ère rangée et les 2 derniers
    // (Crèche/Maternelle) la 2e. Coïncide avec LEVEL_ORDER : tout niveau hors de cette liste
    // (donnée héritée, future nomenclature) reste géré par otherGroups plutôt que silencieusement perdu.
    const MAIN_CYCLES = ['Primaire', 'Collège', 'Lycée', 'Crèche', 'Maternelle'];

    const escapeRegex = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

    // Valeurs de départ du formulaire « Nouvelle classe » (voir resetNewClassroom).
    const DEFAULT_LEVEL = 'Primaire';
    const DEFAULT_CAPACITY = 30;

    // Plage Unicode des diacritiques combinants (U+0300 à U+036F), construite par code plutôt que par
    // un littéral dans le code source — un pipeline de rendu texte en amont réécrit silencieusement
    // cette séquence d'échappement en un vrai caractère combinant, corrompant la regex.
    const COMBINING_MARKS = new RegExp('[' + String.fromCharCode(768) + '-' + String.fromCharCode(879) + ']', 'g');
    const stripAccents = (s) => s.normalize('NFD').replace(COMBINING_MARKS, '');

    // Libellés COMPLETS d'affichage, DANS L'ORDRE PÉDAGOGIQUE du cycle (6e → 3e, Seconde →
    // Terminale). Chaque école saisit ses classes à sa façon (« 6e A », « 6 eme A », « Sixième A »,
    // « 2nde S », « Tle L2 ») : la règle reconnaît ces variantes — chiffre, accents et espace
    // intercalaire optionnels — et l'écran affiche toujours le libellé long, le suffixe de série
    // (« A », « S1 », « L2 ») étant conservé tel qu'il a été saisi. Un nom déjà complet ne change pas.
    // Le nom réel (recherche, édition, suppression, API) reste inchangé — seule l'étiquette est
    // développée ; le nom enregistré reste lisible via l'attribut title (survol) de la même ligne.
    // Table PAR CYCLE, jamais globale : « 2 A » n'est une « Seconde » qu'au Lycée. Primaire et
    // Maternelle n'y figurent pas — « CE1 », « CM2 » ou « GS » sont déjà les libellés usuels.
    const CLASS_FULL_NAMES = {
        'Collège': [
            [/^(?:6\s*(?:eme|e)?|sixieme)(?=\s|$)/i, 'Sixième'],
            [/^(?:5\s*(?:eme|e)?|cinquieme)(?=\s|$)/i, 'Cinquième'],
            [/^(?:4\s*(?:eme|e)?|quatrieme)(?=\s|$)/i, 'Quatrième'],
            [/^(?:3\s*(?:eme|e)?|troisieme)(?=\s|$)/i, 'Troisième']
        ],
        'Lycée': [
            [/^(?:2\s*(?:nde|nd|de|e)?|seconde)(?=\s|$)/i, 'Seconde'],
            [/^(?:1\s*(?:ere|re|er|e)?|premiere)(?=\s|$)/i, 'Première'],
            [/^(?:t(?:le|erm)|terminale)(?=\s|$)/i, 'Terminale']
        ]
    };

    /**
     * Étiquette affichée pour une classe : libellé de niveau en toutes lettres + suffixe d'origine.
     * Le cycle est requis — c'est lui qui décide de la table de règles applicable.
     */
    function displayName(name, level) {
        const rules = CLASS_FULL_NAMES[level];
        if (!rules) return name;

        // Les règles sont écrites SANS accents et testées sur le nom désaccentué (« 6ème » ≡ « 6eme »,
        // « Premiere » ≡ « Première »). stripAccents ne change pas le nombre de caractères : la
        // longueur reconnue découpe donc aussi le nom d'origine, dont on garde la fin telle quelle.
        const trimmed = name.trim();
        const normalized = stripAccents(trimmed);
        const match = rules.find(([re]) => re.test(normalized));
        return match ? match[1] + trimmed.slice(normalized.match(match[0])[0].length) : name;
    }

    // Succession pédagogique RÉELLE (CE1 < CI alphabétiquement serait faux, « 3ème » remonterait
    // devant « 6ème ») : chaque classe est rattachée au premier jeton dont son libellé long (voir
    // displayName) part — ex. « CE1 B » → CE1, « Tle S1 » → Terminale. Collège et Lycée reprennent
    // l'ordre de CLASS_FULL_NAMES : une seule source pour l'affichage ET pour le tri, impossible de
    // corriger l'un en oubliant l'autre. Un rang peut lister plusieurs jetons alternatifs (Maternelle :
    // sigle ou nom complet). Une classe hors nomenclature (ex. « Test Classe Verif ») part en fin de cycle.
    const GRADE_ORDER = {
        'Maternelle': [['TPS', 'Toute Petite Section'], ['PS', 'Petite Section'], ['MS', 'Moyenne Section'], ['GS', 'Grande Section']],
        'Primaire': ['CI', 'CP', 'CE1', 'CE2', 'CM1', 'CM2'],
        'Collège': CLASS_FULL_NAMES['Collège'].map(([, label]) => label),
        'Lycée': CLASS_FULL_NAMES['Lycée'].map(([, label]) => label)
    };

    function gradeRank(level, name) {
        const tokens = GRADE_ORDER[level];
        if (!tokens) return -1;
        const label = displayName(name, level).trim();
        return tokens.findIndex((entry) => (Array.isArray(entry) ? entry : [entry])
            .some((token) => new RegExp(`^${escapeRegex(token)}(\\s|$)`, 'i').test(label)));
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
            level: DEFAULT_LEVEL,
            capacity: DEFAULT_CAPACITY
        },
        createErrors: {},

        // Confirmation « Classe ajoutée » affichée après un enregistrement réussi.
        showAddedDialog: false,
        addedClassroomName: '',

        // Créer, corriger ou archiver une classe : réservé au Directeur et au Secrétariat côté
        // serveur (ClassroomsController.ManageRoles) — l'Enseignant consulte l'arborescence des
        // classes mais ne la modifie pas ; ce booléen n'est qu'un confort d'affichage.
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
            // La recherche porte sur le nom enregistré ET sur l'étiquette affichée : la liste montre
            // « Seconde S », taper « Seconde » doit la trouver même si elle est enregistrée « 2nde S ».
            const visible = this.classrooms.filter((c) =>
                (!q || c.name.toLowerCase().includes(q) || displayName(c.name, c.level).toLowerCase().includes(q) ||
                    c.level.toLowerCase().includes(q)) &&
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
         * Grille principale de l'écran : toujours les 5 cycles dans l'ordre MAIN_CYCLES (Primaire/
         * Collège/Lycée puis Crèche/Maternelle), même quand un cycle n'a encore aucune classe — la
         * carte reste visible avec son en-tête, plutôt que de faire sauter la mise en page selon
         * les données du moment.
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

        /**
         * Vide le formulaire de création. `keepLevel` : on enchaîne une autre classe — le cycle saisi
         * est conservé, on crée en général toutes les classes d'un même cycle à la suite (resélectionner
         * « Lycée » à chaque classe est une friction inutile). Sinon on repart du niveau par défaut :
         * la modale a été quittée, la saisie précédente n'a plus à survivre.
         */
        resetNewClassroom(keepLevel = false) {
            this.newClassroom = {
                name: '',
                level: keepLevel ? this.newClassroom.level : (localStorage.getItem('classrooms_lastLevel') || DEFAULT_LEVEL),
                capacity: DEFAULT_CAPACITY
            };
            this.createErrors = {};
        },

        openCreate() {
            this.isCreateOpen = true;
        },

        /** Sortie explicite (Annuler, ✕, fond, Échap) : le formulaire repart de zéro, niveau compris. */
        closeCreate() {
            this.isCreateOpen = false;
            this.resetNewClassroom();
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                await window.api.post('/classrooms', this.newClassroom);

                localStorage.setItem('classrooms_lastLevel', this.newClassroom.level);
                
                this.isCreateOpen = false;
                this.addedClassroomName = this.newClassroom.name;
                this.resetNewClassroom(true); // enchaînement possible : on garde le cycle
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
            if (!classroom) return;
            this.deletingClassroom = { id: classroom.id, name: classroom.name, rowVersion: classroom.rowVersion };
            this.deleteClassroomError = null;
        },

        deleteClassroom(classroom) {
            this.openDelete(classroom);
        },

        closeDelete() {
            this.deletingClassroom = null;
            this.deleteClassroomError = null;
        },

        async confirmDelete(classroom = null) {
            if (classroom && classroom.id) {
                this.openDelete(classroom);
                return;
            }
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

        // ------------------------------------------------------------ Téléchargement Cartes Scolaires
        
        async downloadSchoolCards(classroom) {
            try {
                const response = await fetch(`/api/v1/classrooms/${classroom.id}/school-cards`, {
                    headers: { 'Authorization': `Bearer ${window.auth.getToken()}` }
                });
                
                if (!response.ok) {
                    throw new Error('Erreur lors de la génération des cartes scolaires.');
                }
                
                const blob = await response.blob();
                const url = window.URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.style.display = 'none';
                a.href = url;
                a.download = `Cartes_Scolaires_${classroom.name}.pdf`;
                document.body.appendChild(a);
                a.click();
                window.URL.revokeObjectURL(url);
            } catch (err) {
                console.error(err);
                alert('Impossible de télécharger les cartes scolaires. Vérifiez qu\'il y a bien des élèves inscrits dans cette classe pour l\'année en cours.');
            }
        },

        // Utilities
        getTotalCapacity() {
            return this.classrooms.reduce((sum, c) => sum + (c.capacity || 0), 0);
        },

        plural(count, singular, plural) {
            return count + ' ' + (count > 1 ? plural : singular);
        },

        displayName(name, level) {
            return displayName(name, level);
        }
    }));
});
