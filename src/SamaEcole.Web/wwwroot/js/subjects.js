/**
 * Écran Matières et coefficients — ticket JGK-C03. Écriture ouverte à Directeur, Secrétariat et
 * Enseignant, sans condition de réglage par école (contrairement au barème/mentions, restés derrière
 * GradingPolicies.CanManageGradingScale) — SubjectsController est [Authorize(Roles =
 * "Directeur,Secretariat,Enseignant")] en écriture. Le serveur reste seul juge : ce fichier ne fait
 * que masquer les boutons pour la Finance, jamais une mesure de sécurité en soi.
 */
document.addEventListener('alpine:init', () => {
    // Les 4 colonnes fixes de la grille de l'écran Matières, gauche → droite.
    const CATEGORY_ORDER = ['Lettres & Langues', 'Sciences & Technologies', 'Arts, Sport & Culture', 'Éveil & Petite Enfance'];

    // « Éveil & Petite Enfance » se décide par NIVEAU, pas par mot-clé dans le nom : une matière
    // « Éveil scientifique » de niveau Primaire (donnée réelle de l'école) contient le mot « éveil »
    // mais n'a rien à voir avec la Crèche/Maternelle — un classement par mot-clé l'y enverrait à tort.
    const EARLY_CHILDHOOD_LEVELS = ['Crèche', 'Maternelle'];

    // Classement des 3 autres colonnes par mot-clé dans le nom de la matière. Le nom d'une matière
    // est un champ LIBRE par école (pas d'énum, voir formulaire Créer/Modifier) : cette liste est une
    // heuristique de lecture, pas une vérité absolue — une matière qui ne correspond à aucun mot-clé
    // part dans otherSubjects (filet de sécurité) plutôt que d'être forcée dans une des 4 colonnes.
    const CATEGORY_KEYWORDS = {
        'Lettres & Langues': ['francais', 'anglais', 'arabe', 'wolof', 'espagnol', 'allemand', 'italien', 'portugais', 'chinois', 'langue', 'langues', 'histoire', 'geographie', 'geo', 'litterature', 'philosophie', 'philo', 'lettres', 'lv1', 'lv2'],
        'Sciences & Technologies': ['mathematiques', 'maths', 'math', 'physique', 'chimie', 'pc', 'svt', 'sciences', 'science', 'scientifique', 'informatique', 'technologie', 'technologies', 'biologie', 'numerique', 'sti'],
        'Arts, Sport & Culture': ['eps', 'sport', 'sportive', 'arts', 'musique', 'dessin', 'civique', 'morale', 'culture', 'theatre', 'danse']
    };

    // Ordre de VÉRIFICATION des mots-clés (distinct de CATEGORY_ORDER, qui fixe l'ordre d'AFFICHAGE
    // des colonnes) : « Arts, Sport & Culture » est vérifié en premier à cause d'un faux-ami réel —
    // « Éducation Physique et Sportive » contient le mot « physique » (Sciences), mais désigne le
    // sport, pas la physique. Les mots-clés « eps »/« sportive », sans ambiguïté, doivent gagner
    // avant que « physique », plus générique, ne classe la matière à tort en Sciences.
    const KEYWORD_MATCH_ORDER = ['Arts, Sport & Culture', 'Lettres & Langues', 'Sciences & Technologies'];

    // Plage Unicode des diacritiques combinants (U+0300 à U+036F), construite par code plutôt que par
    // un littéral ̀-ͯ dans le code source — un pipeline de rendu texte en amont réécrit
    // silencieusement cette séquence d'échappement en un vrai caractère combinant, corrompant la regex.
    const COMBINING_MARKS = new RegExp('[' + String.fromCharCode(768) + '-' + String.fromCharCode(879) + ']', 'g');
    const stripAccents = (s) => s.normalize('NFD').replace(COMBINING_MARKS, '');

    function categoryFor(subject) {
        if (EARLY_CHILDHOOD_LEVELS.includes(subject.level)) return 'Éveil & Petite Enfance';

        const normalized = stripAccents(subject.name).toLowerCase();
        for (const category of KEYWORD_MATCH_ORDER) {
            const keywords = CATEGORY_KEYWORDS[category];
            if (keywords && keywords.some((kw) => new RegExp(`\\b${kw}\\b`).test(normalized))) return category;
        }
        return null; // ni Crèche/Maternelle, ni un mot-clé connu → otherSubjects
    }

    // Abrégés D'AFFICHAGE seulement (grille compacte) : « Physique-Chimie » → « PC », « Histoire-
    // Géographie » → « Hist-Géo »… Comparaison sur le nom normalisé (accents/casse/tirets/espaces
    // ignorés). Le nom réel (recherche, édition, suppression, API) reste inchangé ; le nom complet
    // reste lisible via l'attribut title (survol) posé sur la même ligne. Ne couvre QUE les
    // intitulés standardisés listés ici — le nom d'une matière est un champ libre par école (pas
    // d'énum), donc un nom inconnu garde son texte intégral, laissé à la troncature visuelle CSS
    // (`truncate`) de la grille plutôt qu'à une abréviation devinée au hasard.
    const SUBJECT_ABBREVIATIONS = [
        [/^physique[\s-]*chimie$/, 'PC'],
        [/^maths?(ematiques?)?$/, 'Maths'],
        [/^histoire[\s-]*geographie$/, 'Hist-Géo'],
        [/^sciences? de la vie et de la terre$/, 'SVT'],
        [/^education physique et sportive$/, 'EPS']
    ];

    function displayName(name) {
        const normalized = stripAccents(name.trim()).toLowerCase();
        const match = SUBJECT_ABBREVIATIONS.find(([re]) => re.test(normalized));
        return match ? match[1] : name;
    }

    /**
     * Un champ de formulaire laissé vide vaut '' en HTML — y compris le <select> « Aucun domaine » et
     * les champs nombre. Le serveur, lui, distingue « absent » (null) de « vide » : un '' sur
     * parentSubjectId ne se lit pas comme un Guid, et un '' sur maxScore serait un barème nul.
     * Les blancs deviennent donc null AVANT l'envoi, une fois pour toutes plutôt qu'un champ à la fois.
     */
    function blankToNull(payload) {
        const cleaned = { ...payload };
        for (const key of Object.keys(cleaned)) {
            if (cleaned[key] === '' || cleaned[key] === undefined || Number.isNaN(cleaned[key])) {
                cleaned[key] = null;
            }
        }
        return cleaned;
    }

    Alpine.data('subjectsView', () => ({
        subjects: [],
        isLoading: false,
        error: null,
        search: '',

        get canCreateSubject() {
            return ['Directeur', 'Secretariat', 'Enseignant'].includes(window.auth.role);
        },

        // Corriger/archiver une matière déjà créée partage EXACTEMENT la même permission que la
        // création côté serveur : un seul getter suffit.
        get canManageSubject() {
            return this.canCreateSubject;
        },

        isCreateOpen: false,
        isSubmitting: false,
        newSubject: { name: '', level: '', coefficient: 1, parentSubjectId: '', maxScore: '', displayOrder: 0 },
        createErrors: {},

        // ── Structure d'évaluation (grilles APC du primaire) ──
        // Deux lectures du MÊME jeu de matières : la grille par catégories (l'écran d'origine, qui
        // ignore entièrement la hiérarchie) et l'arbre domaine → activités, seul endroit d'où l'on
        // configure les barèmes par ligne, l'ordre et les entêtes de colonnes du bulletin.
        viewMode: 'categories',
        structureLevel: '',
        isReordering: false,
        structureError: null,
        headerDraft: { column1Header: '', column2Header: '' },
        isSavingHeaders: false,

        // Confirmation « Matière ajoutée » affichée après un enregistrement réussi.
        showAddedDialog: false,
        addedSubjectName: '',

        // Édition (modale)
        editing: null, // { id, name, level, coefficient, rowVersion, parentSubjectId, maxScore, displayOrder, … }
        isSavingEdit: false,
        editErrors: {},
        showEditedDialog: false,
        editedSubjectName: '',

        // Suppression (modale de confirmation)
        deletingSubject: null, // { id, name, rowVersion }
        isDeletingSubject: false,
        deleteSubjectError: null,
        showDeletedDialog: false,
        deletedSubjectName: '',

        init() {
            this.loadSubjects();
        },

        async loadSubjects() {
            this.isLoading = true;
            this.error = null;
            try {
                this.subjects = await window.api.get('/subjects');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des matières.');
            } finally {
                this.isLoading = false;
            }
        },

        get visibleSubjects() {
            const q = this.search.trim().toLowerCase();
            return q
                ? this.subjects.filter((s) => s.name.toLowerCase().includes(q) || s.level.toLowerCase().includes(q))
                : this.subjects;
        },

        /**
         * Grille principale de l'écran : toujours 4 colonnes, une par catégorie de disciplines
         * (Lettres & Langues / Sciences & Technologies / Arts, Sport & Culture / Éveil & Petite
         * Enfance — voir categoryFor), même quand une catégorie n'a encore aucune matière. Triées
         * par ordre alphabétique à l'intérieur de leur colonne (consigne explicite : une recherche
         * rapide plutôt que l'ordre pédagogique des cycles, puisque plusieurs niveaux se mélangent
         * désormais dans une même colonne).
         */
        get categoryGroups() {
            const byCategory = new Map();
            for (const subject of this.visibleSubjects) {
                const category = categoryFor(subject);
                if (!category) continue; // → otherSubjects
                if (!byCategory.has(category)) byCategory.set(category, []);
                byCategory.get(category).push(subject);
            }

            return CATEGORY_ORDER.map((category) => {
                const subjects = (byCategory.get(category) || []).slice().sort((a, b) => a.name.localeCompare(b.name, 'fr'));
                return {
                    category,
                    subjects,
                    totalCoefficient: subjects.reduce((sum, s) => sum + Number(s.coefficient), 0)
                };
            });
        },

        /** Filet de sécurité : une matière dont le nom ne correspond à aucun mot-clé connu (voir
         *  categoryFor) — affichée à part plutôt que forcée dans une des 4 colonnes ou perdue. */
        get otherSubjects() {
            return this.visibleSubjects.filter((s) => !categoryFor(s)).sort((a, b) => a.name.localeCompare(b.name, 'fr'));
        },

        /** Nombre de niveaux distincts parmi les matières visibles (tuile récapitulative « Niveaux
         *  couverts ») — indépendant du classement par catégorie de la grille. */
        get visibleLevelCount() {
            return new Set(this.visibleSubjects.map((s) => s.level)).size;
        },

        // ────────────────────────────────────────────────────────── Structure d'évaluation

        /** Tous les niveaux configurés, dans l'ordre d'affichage des chips de sélection. */
        get structureLevels() {
            return [...new Set(this.subjects.map((s) => s.level))].sort((a, b) => a.localeCompare(b, 'fr'));
        },

        /**
         * L'arbre du niveau sélectionné : domaines de premier niveau, chacun avec ses activités.
         * L'ordre est celui que l'école a fixé (displayOrder), le nom ne départageant qu'à égalité —
         * exactement la règle qu'applique le serveur, sans quoi l'écran mentirait sur ce que le
         * bulletin imprimera.
         */
        get structureGroups() {
            const level = this.structureLevel;
            const atLevel = this.subjects.filter((s) => s.level === level);
            const byOrder = (a, b) => (a.displayOrder - b.displayOrder) || a.name.localeCompare(b.name, 'fr');

            return atLevel
                .filter((s) => !s.parentSubjectId)
                .sort(byOrder)
                .map((root) => ({
                    root,
                    children: atLevel.filter((s) => s.parentSubjectId === root.id).sort(byOrder)
                }));
        },

        /** Une grille est « hiérarchique » — et le bulletin bascule sur le tableau APC — dès qu'une
         *  activité est rattachée à un domaine à ce niveau. C'est la règle exacte du serveur. */
        get isHierarchicalLevel() {
            return this.subjects.some((s) => s.level === this.structureLevel && s.parentSubjectId);
        },

        get structureLineCount() {
            return this.structureGroups.reduce((n, g) => n + Math.max(g.children.length, 1), 0);
        },

        /** Les entêtes vivent sur le PREMIER domaine du niveau (voir EvaluationStructureBuilder) : c'est
         *  lui que l'écran modifie quand on enregistre les libellés de colonnes. */
        get headerCarrier() {
            const groups = this.structureGroups;
            return groups.length ? groups[0].root : null;
        },

        selectStructureLevel(level) {
            this.structureLevel = level;
            this.structureError = null;
            const carrier = this.headerCarrier;
            this.headerDraft = {
                column1Header: (carrier && carrier.column1Header) || '',
                column2Header: (carrier && carrier.column2Header) || ''
            };
        },

        showStructure() {
            this.viewMode = 'structure';
            const level = this.structureLevels.includes(this.structureLevel)
                ? this.structureLevel
                : (this.structureLevels[0] || '');

            // Toujours re-sélectionner, même si le niveau n'a pas changé : c'est ce qui recharge les
            // entêtes de colonnes dans le formulaire depuis l'état réel des matières.
            this.selectStructureLevel(level);
        },

        /**
         * Libellé du barème d'une ligne : « Sur 40 » quand l'école en a fixé un, « Barème du cycle »
         * sinon — et non « Sur — », qui laisserait croire à une donnée manquante là où c'est un choix
         * (la ligne suit le /10 du primaire ou le /20 du secondaire, voir Subject.MaxScore).
         */
        maxScoreLabel(subject) {
            const maxScore = subject && subject.maxScore;
            return maxScore === null || maxScore === undefined || maxScore === ''
                ? 'Barème du cycle'
                : `Sur ${Number(maxScore).toLocaleString('fr-FR')}`;
        },

        /** Ouvre la création d'une ACTIVITÉ sous un domaine : le niveau et le parent sont imposés. */
        openCreateActivity(root) {
            const siblings = (this.structureGroups.find((g) => g.root.id === root.id) || {}).children || [];
            this.newSubject = {
                name: '',
                level: root.level,
                coefficient: root.coefficient,
                parentSubjectId: root.id,
                // Placée en fin de fratrie : une activité ajoutée ne vient jamais s'intercaler au milieu
                // d'une grille déjà ordonnée.
                displayOrder: siblings.length + 1,
                maxScore: siblings.length ? siblings[siblings.length - 1].maxScore : null
            };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        /** Ouvre la création d'un DOMAINE au niveau affiché. */
        openCreateGroup() {
            this.newSubject = {
                name: '',
                level: this.structureLevel,
                coefficient: 1,
                parentSubjectId: '',
                displayOrder: this.structureGroups.length + 1,
                maxScore: null
            };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        /**
         * Réordonne une fratrie (les domaines d'un niveau, ou les activités d'un domaine) d'un cran.
         *
         * Les rangs sont RENUMÉROTÉS de 1 à n après le déplacement, plutôt qu'échangés deux à deux :
         * une grille dont toutes les lignes sont encore à 0 (le cas de toute matière antérieure à cette
         * option) n'a aucun rang à échanger, et un simple échange n'y produirait aucun mouvement visible.
         * Seules les lignes dont le rang change réellement sont enregistrées.
         */
        async moveSubject(siblings, index, direction) {
            const target = index + direction;
            if (this.isReordering || target < 0 || target >= siblings.length) return;

            const ordered = siblings.slice();
            const [moved] = ordered.splice(index, 1);
            ordered.splice(target, 0, moved);

            this.isReordering = true;
            this.structureError = null;
            try {
                for (let i = 0; i < ordered.length; i++) {
                    if (ordered[i].displayOrder === i + 1) continue;
                    await this.saveSubject(ordered[i], { displayOrder: i + 1 });
                }
                await this.loadSubjects();
            } catch (err) {
                this.structureError = this.reorderMessage(err);
                await this.loadSubjects();
            } finally {
                this.isReordering = false;
            }
        },

        /** Enregistre les entêtes de colonnes du bulletin sur le premier domaine du niveau. */
        async saveHeaders() {
            const carrier = this.headerCarrier;
            if (!carrier) return;

            this.isSavingHeaders = true;
            this.structureError = null;
            try {
                await this.saveSubject(carrier, {
                    column1Header: this.headerDraft.column1Header.trim() || null,
                    column2Header: this.headerDraft.column2Header.trim() || null
                });
                await this.loadSubjects();
            } catch (err) {
                this.structureError = this.reorderMessage(err);
            } finally {
                this.isSavingHeaders = false;
            }
        },

        /**
         * PUT d'une matière en ne changeant que les champs de `patch`. Le PUT étant un remplacement
         * complet, TOUS les champs de structure doivent repartir — en omettre un le remettrait à sa
         * valeur par défaut (un domaine perdrait ses activités, un barème /40 redeviendrait celui du
         * cycle) sans que personne ne l'ait demandé.
         */
        saveSubject(subject, patch) {
            const payload = {
                name: subject.name,
                level: subject.level,
                coefficient: subject.coefficient,
                rowVersion: subject.rowVersion,
                parentSubjectId: subject.parentSubjectId,
                maxScore: subject.maxScore,
                displayOrder: subject.displayOrder ?? 0,
                column1Header: subject.column1Header,
                column2Header: subject.column2Header,
                ...patch
            };

            return window.api.put(`/subjects/${subject.id}`, blankToNull(payload));
        },

        reorderMessage(err) {
            if (err && err.code === 'CONCURRENCY_CONFLICT') {
                return 'La structure vient d\'être modifiée par un autre utilisateur. Elle a été rafraîchie — réessayez.';
            }
            return (err && err.message) || 'Erreur lors de l\'enregistrement de la structure.';
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

        displayName(name) {
            return displayName(name);
        },

        openCreate() {
            // Reprend le dernier niveau saisi : on crée en général toutes les matières d'un niveau à
            // la suite. Repartir d'un champ vide à chaque fois ferait retaper « Primaire » dix fois.
            const lastLevel = this.subjects.length ? this.subjects[this.subjects.length - 1].level : '';
            this.newSubject = {
                name: '', level: lastLevel, coefficient: 1,
                parentSubjectId: '', maxScore: '', displayOrder: 0
            };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        /** Les domaines auxquels une nouvelle ligne peut se rattacher : ceux du niveau saisi, et eux
         *  seuls — une activité vit toujours au niveau de son domaine (SubjectHierarchyGuard). */
        get parentOptionsForNewSubject() {
            return this.subjects
                .filter((s) => !s.parentSubjectId && s.level === this.newSubject.level)
                .sort((a, b) => (a.displayOrder - b.displayOrder) || a.name.localeCompare(b.name, 'fr'));
        },

        /** Idem pour la modale d'édition, en excluant la matière elle-même. */
        get parentOptionsForEditing() {
            if (!this.editing) return [];
            return this.subjects
                .filter((s) => !s.parentSubjectId && s.level === this.editing.level && s.id !== this.editing.id)
                .sort((a, b) => (a.displayOrder - b.displayOrder) || a.name.localeCompare(b.name, 'fr'));
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                await window.api.post('/subjects', blankToNull(this.newSubject));

                this.isCreateOpen = false;
                this.addedSubjectName = this.newSubject.name;
                await this.loadSubjects();
                this.showAddedDialog = true; // confirmation « Matière ajoutée »
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la matière.');
            } finally {
                this.isSubmitting = false;
            }
        },

        // ------------------------------------------------------------ Édition

        openEdit(subject) {
            this.editing = {
                id: subject.id,
                name: subject.name,
                level: subject.level,
                coefficient: subject.coefficient,
                rowVersion: subject.rowVersion,
                parentSubjectId: subject.parentSubjectId ?? '',
                maxScore: subject.maxScore ?? null,
                displayOrder: subject.displayOrder ?? 0,
                column1Header: subject.column1Header ?? null,
                column2Header: subject.column2Header ?? null
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
                // TOUS les champs de structure repartent : le PUT remplace la matière entière, en
                // omettre un le remettrait à sa valeur par défaut — corriger un simple libellé
                // détacherait l'activité de son domaine et lui ferait perdre son barème.
                await this.saveSubject(this.editing, {});
                this.editedSubjectName = this.editing.name;
                this.closeEdit();
                await this.loadSubjects();
                this.showEditedDialog = true;
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editErrors = { global: 'Cette matière vient d\'être modifiée par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez.' };
                    await this.loadSubjects();
                } else {
                    this.editErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingEdit = false;
            }
        },

        // ------------------------------------------------------------ Suppression

        openDelete(subject) {
            if (!subject) return;
            this.deletingSubject = { id: subject.id, name: subject.name, rowVersion: subject.rowVersion };
            this.deleteSubjectError = null;
        },

        deleteSubject(subject) {
            this.openDelete(subject);
        },

        closeDelete() {
            this.deletingSubject = null;
            this.deleteSubjectError = null;
        },

        async confirmDelete(subject = null) {
            if (subject && subject.id) {
                this.openDelete(subject);
                return;
            }
            if (!this.deletingSubject) return;

            this.isDeletingSubject = true;
            this.deleteSubjectError = null;
            try {
                await window.api.delete(`/subjects/${this.deletingSubject.id}?rowVersion=${this.deletingSubject.rowVersion}`);
                this.deletedSubjectName = this.deletingSubject.name;
                this.deletingSubject = null;
                await this.loadSubjects();
                this.showDeletedDialog = true;
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    // Des notes existent déjà pour cette matière (DeleteSubjectCommandHandler) : le
                    // message serveur est déjà explicite, on l'affiche tel quel.
                    this.deleteSubjectError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteSubjectError = 'Cette matière vient d\'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.';
                    await this.loadSubjects();
                } else {
                    this.deleteSubjectError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingSubject = false;
            }
        }
    }));
});
