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

    // ── Cycles d'enseignement (filtre supérieur de l'écran) ───────────────────────────────────────
    // Les domaines (Lettres & Langues, Sciences & Technologies…) traversent TOUS les cycles : sans
    // second axe, l'Anglais du Lycée et la « Langue et Communication » du Primaire se lisent dans la
    // même colonne, alors qu'ils ne se gèrent jamais ensemble. Le cycle est donc un FILTRE, pas une
    // catégorie de plus — la grille par domaines reste la seule structure de la page.
    //
    // Le niveau d'une matière est choisi dans une liste fermée à l'écran (Crèche, Maternelle,
    // Primaire, Collège, Lycée) mais reste un TEXTE LIBRE côté API (CreateSubjectCommandValidator
    // n'impose aucune liste, et le <select> est postérieur à une partie des données) : la
    // reconnaissance ci-dessous ignore casse et accents, et les synonymes sont EXACTEMENT ceux du
    // serveur (ClassroomCycle.ByLevel) — « Élémentaire » pour le primaire, « Moyen »/« CEM » pour
    // le collège, « Secondaire » pour le lycée. Sans quoi l'écran Classes et l'écran Matières
    // rangeraient un même niveau dans deux cycles différents.
    //
    // Ordre d'affichage : celui de l'écran Classes (classrooms.js, LEVEL_ORDER) — Primaire, Collège
    // et Lycée en tête, préscolaire en dernier.
    const ALL_CYCLES = 'all';

    const CYCLES = [
        { key: 'primaire', label: 'Primaire', levels: ['primaire', 'elementaire', 'ecole elementaire', 'ecole primaire'] },
        { key: 'college', label: 'Collège', levels: ['college', 'moyen', 'cem'] },
        { key: 'lycee', label: 'Lycée', levels: ['lycee', 'secondaire'] },
        { key: 'prescolaire', label: 'Préscolaire', levels: ['creche', 'maternelle', 'prescolaire'] }
    ];

    // Filet de sécurité, même esprit qu'otherSubjects pour les catégories : un niveau hors
    // nomenclature obtient son propre onglet plutôt que d'être rangé de force dans un cycle ou —
    // pire — de disparaître de tous les onglets. L'onglet n'apparaît que s'il contient quelque chose.
    const OTHER_CYCLE = { key: 'autres', label: 'Autres niveaux', levels: [] };

    function cycleFor(subject) {
        const level = stripAccents(String(subject.level || '').trim()).toLowerCase();
        const cycle = CYCLES.find((c) => c.levels.includes(level));
        return cycle ? cycle.key : OTHER_CYCLE.key;
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

        // ── Filtre par cycle (barre d'onglets au-dessus de la grille) ──
        // 'all' ou la clé d'un cycle (voir CYCLES). `requestedCycle` retient le ?cycle= de l'URL le
        // temps du premier chargement : les onglets réellement proposés dépendent des matières, on
        // ne peut donc valider la demande qu'une fois la liste connue (voir resolveInitialCycle).
        cycle: ALL_CYCLES,
        requestedCycle: null,
        hasResolvedCycle: false,

        get canCreateSubject() {
            return ['Directeur', 'Secretariat', 'Enseignant'].includes(window.auth.role);
        },

        // Onglet « Coefficients » (Évolution N°4) : lecture Directeur + Secrétariat, écriture Directeur seul
        // (CoefficientsController). L'Enseignant, qui gère les matières, n'a pas accès à cette grille.
        get canViewCoefficients() {
            return ['Directeur', 'Secretariat'].includes(window.auth.role);
        },

        showCoefficients() {
            this.viewMode = 'coefficients';
        },

        // Corriger/archiver une matière déjà créée partage EXACTEMENT la même permission que la
        // création côté serveur : un seul getter suffit.
        get canManageSubject() {
            return this.canCreateSubject;
        },

        isCreateOpen: false,
        isSubmitting: false,
        newSubject: { name: '', nameAr: '', level: '', coefficient: 1, parentSubjectId: '', maxScore: '', displayOrder: 0 },
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

        // ── Drag & drop de la structure d'évaluation (poignée GripVertical, remplace les flèches ↑/↓) ──
        // Périmètre du glissement en cours : 'groups' pour la liste des domaines, ou l'id du domaine
        // pour la liste de ses activités. On refuse de déposer une activité dans la liste des
        // domaines (ou l'inverse) : la fratrie de départ et celle d'arrivée doivent coïncider.
        dragScope: null,
        dragIndex: null,
        dragOverIndex: null,

        // ── Sections rétractables (Collapsible) ──
        // Vue « Matières » : catégories repliées (clé = nom de catégorie) + bloc « Autres matières ».
        // Vue « Structure » : domaines repliés (clé = id du domaine). Objets simples pour rester
        // réactifs sous Alpine ; l'état de dépli n'est pas persisté (confort de session).
        collapsedCategories: {},
        otherCollapsed: false,
        collapsedNodes: {},

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
            // Deep-link « badge de matière » : les badges Matières d'autres écrans (colonne Matières
            // de la liste Enseignants, emploi du temps…) pointent vers /matieres?q=<nom>. On arrive
            // alors avec la recherche déjà remplie, la grille se limite à cette matière. Faute de
            // page de détail propre à une matière, c'est la vue la plus ciblée que l'écran propose.
            const params = new URLSearchParams(window.location.search);
            const q = params.get('q');
            if (q) this.search = q;
            this.requestedCycle = params.get('cycle');
            this.loadSubjects();
        },

        async loadSubjects() {
            this.isLoading = true;
            this.error = null;
            try {
                this.subjects = await window.api.get('/subjects');
                if (!this.hasResolvedCycle) this.resolveInitialCycle();
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des matières.');
            } finally {
                this.isLoading = false;
            }
        },

        // ────────────────────────────────────────────────────────────── Filtre par cycle

        /**
         * Cycle affiché au premier chargement. Il ne peut se décider qu'une fois les matières
         * connues — il dépend des cycles que l'école couvre réellement :
         *  - `?cycle=` dans l'URL gagne toujours (lien copié, onglet rouvert), s'il désigne un cycle
         *    effectivement couvert ;
         *  - `?q=` (deep-link depuis un badge de matière d'un autre écran — voir init) impose « Tous
         *    les cycles » : la matière cherchée appartient à n'importe quel cycle, et un onglet
         *    présélectionné la ferait disparaître de la recherche qu'on vient tout juste d'ouvrir ;
         *  - une école qui ne couvre qu'un cycle n'a rien à filtrer → « Tous les cycles » ;
         *  - sinon le premier cycle couvert, parce que c'est précisément le mélange Primaire/Lycée
         *    dans une même colonne que ce filtre existe pour éviter. Le nombre de matières porté par
         *    chaque onglet dit tout de suite où sont les autres.
         */
        resolveInitialCycle() {
            this.hasResolvedCycle = true;
            const active = this.activeCycles;
            const wanted = this.requestedCycle;

            if (wanted === ALL_CYCLES || (wanted && active.some((c) => c.key === wanted))) {
                this.cycle = wanted;
                return;
            }

            this.cycle = (this.search.trim() || active.length < 2) ? ALL_CYCLES : active[0].key;
        },

        /** Les cycles réellement couverts par l'école, dans l'ordre d'affichage. Calculé sur TOUTES
         *  les matières (pas sur la recherche) : la barre d'onglets ne doit pas se réorganiser sous
         *  les doigts pendant qu'on tape. */
        get activeCycles() {
            const present = new Set(this.subjects.map(cycleFor));
            return [...CYCLES, OTHER_CYCLE].filter((c) => present.has(c.key));
        },

        /** Onglets de cycle, avec le nombre de matières que chacun contient POUR LA RECHERCHE EN
         *  COURS — un onglet à 0 pendant une recherche indique où la matière n'est pas. */
        get cycleTabs() {
            const searched = this.searchedSubjects;
            return this.activeCycles.map((c) => ({
                key: c.key,
                label: c.label,
                count: searched.filter((s) => cycleFor(s) === c.key).length
            }));
        },

        /** Libellé du cycle affiché, pour les messages (état vide). */
        get currentCycleLabel() {
            const cycle = [...CYCLES, OTHER_CYCLE].find((c) => c.key === this.cycle);
            return cycle ? cycle.label : 'Tous les cycles';
        },

        /**
         * Bascule d'onglet et synchronise l'URL via replaceState — un lien copié rouvre le même
         * cycle, sans recharger les matières déjà en mémoire (même procédé que goToTab dans
         * settings.js).
         */
        selectCycle(key) {
            this.cycle = key;
            this.hasResolvedCycle = true;

            const url = new URL(window.location.href);
            if (key === ALL_CYCLES) {
                url.searchParams.delete('cycle');
            } else {
                url.searchParams.set('cycle', key);
            }
            window.history.replaceState({}, '', url);
        },

        /** État visuel d'un onglet de cycle. Renvoie le SEUL modificateur `tab-btn-active` : la base
         *  `.tab-btn` est posée en dur dans la vue (composant partagé, input.css). */
        cycleTabClass(key) {
            return this.cycle === key ? 'tab-btn-active' : '';
        },

        /**
         * Amène l'onglet sur le cycle d'un niveau donné, si la matière qu'on vient d'enregistrer n'y
         * est pas déjà. Sans cela, créer une matière de Lycée depuis l'onglet Primaire (ou déplacer
         * une matière d'un niveau à l'autre) afficherait « Matière ajoutée » suivi d'une grille où
         * elle ne figure nulle part — l'utilisateur conclurait à un échec silencieux.
         */
        revealCycleFor(level) {
            if (this.cycle === ALL_CYCLES) return;
            const target = cycleFor({ level });
            if (target !== this.cycle) this.selectCycle(target);
        },

        /** Matières retenues par la RECHERCHE seule — base de calcul des compteurs d'onglets. */
        get searchedSubjects() {
            const q = this.search.trim().toLowerCase();
            return q
                ? this.subjects.filter((s) => s.name.toLowerCase().includes(q) || s.level.toLowerCase().includes(q))
                : this.subjects;
        },

        /** Matières effectivement affichées : recherche PUIS cycle. Tout le reste de l'écran en
         *  découle (grille par domaines, « Autres matières », tuiles de tête). */
        get visibleSubjects() {
            const subjects = this.searchedSubjects;
            return this.cycle === ALL_CYCLES ? subjects : subjects.filter((s) => cycleFor(s) === this.cycle);
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

            const groups = CATEGORY_ORDER.map((category) => {
                const subjects = (byCategory.get(category) || []).slice().sort((a, b) => a.name.localeCompare(b.name, 'fr'));
                return {
                    category,
                    subjects,
                    totalCoefficient: subjects.reduce((sum, s) => sum + Number(s.coefficient), 0)
                };
            });

            // Sur « Tous les cycles », les 4 colonnes restent affichées même vides : elles disent à
            // l'école quels domaines existent et où sa prochaine matière ira se ranger. Dès qu'un
            // cycle est sélectionné, une colonne vide n'apprend plus rien et coûte une place —
            // « Éveil & Petite Enfance » est vide PAR CONSTRUCTION hors préscolaire, puisque
            // categoryFor y envoie les matières de Crèche/Maternelle et elles seules.
            return this.cycle === ALL_CYCLES ? groups : groups.filter((g) => g.subjects.length > 0);
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
                nameAr: '',
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
                nameAr: '',
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
         * Réordonne une fratrie (les domaines d'un niveau, ou les activités d'un domaine) en
         * déplaçant l'élément `fromIndex` à la position `toIndex`.
         *
         * Les rangs sont RENUMÉROTÉS de 1 à n après le déplacement, plutôt qu'échangés deux à deux :
         * une grille dont toutes les lignes sont encore à 0 (le cas de toute matière antérieure à cette
         * option) n'a aucun rang à échanger, et un simple échange n'y produirait aucun mouvement visible.
         * Seules les lignes dont le rang change réellement sont enregistrées.
         */
        async applyReorder(siblings, fromIndex, toIndex) {
            if (this.isReordering || fromIndex === toIndex
                || fromIndex < 0 || toIndex < 0
                || fromIndex >= siblings.length || toIndex >= siblings.length) return;

            const ordered = siblings.slice();
            const [moved] = ordered.splice(fromIndex, 1);
            ordered.splice(toIndex, 0, moved);

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
                this.endStructureDrag();
            }
        },

        // ─── Glisser-déposer à la souris (poignée GripVertical) ───

        /** Amorce le glissement d'un domaine ('groups') ou d'une activité (id du domaine parent). */
        startStructureDrag(scope, index, event) {
            if (!this.canManageSubject || this.isReordering) return;
            this.dragScope = scope;
            this.dragIndex = index;
            this.dragOverIndex = index;
            if (event && event.dataTransfer) {
                event.dataTransfer.effectAllowed = 'move';
                // Firefox n'amorce un glissement natif que si des données sont posées.
                try { event.dataTransfer.setData('text/plain', String(index)); } catch (_) { /* noop */ }
            }
        },

        /** Autorise le dépôt UNIQUEMENT dans la fratrie d'origine, et mémorise la cible survolée. */
        onStructureDragOver(scope, index, event) {
            if (this.dragScope === null || this.dragScope !== scope) return;
            event.preventDefault();
            if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
            this.dragOverIndex = index;
        },

        /** Dépose l'élément glissé à la position `index` de `siblings`, puis renumérote et enregistre. */
        async dropStructure(scope, index, siblings) {
            if (this.dragScope !== scope || this.dragIndex === null) { this.endStructureDrag(); return; }
            const from = this.dragIndex;
            this.endStructureDrag();
            await this.applyReorder(siblings, from, index);
        },

        endStructureDrag() {
            this.dragScope = null;
            this.dragIndex = null;
            this.dragOverIndex = null;
        },

        // ─── Sections rétractables ───

        toggleCategory(key) {
            this.collapsedCategories[key] = !this.collapsedCategories[key];
        },
        isCategoryCollapsed(key) {
            return !!this.collapsedCategories[key];
        },
        toggleNode(id) {
            this.collapsedNodes[id] = !this.collapsedNodes[id];
        },
        isNodeCollapsed(id) {
            return !!this.collapsedNodes[id];
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
                nameAr: subject.nameAr,
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
            // Quand un cycle est affiché, le niveau proposé est cherché DANS ce cycle : on crée la
            // matière qu'on est en train de regarder, pas celle du dernier import.
            const pool = this.cycle === ALL_CYCLES
                ? this.subjects
                : this.subjects.filter((s) => cycleFor(s) === this.cycle);
            const source = pool.length ? pool : this.subjects;
            const lastLevel = source.length ? source[source.length - 1].level : '';
            this.newSubject = {
                name: '', nameAr: '', level: lastLevel, coefficient: 1,
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
                this.revealCycleFor(this.newSubject.level);
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
                column2Header: subject.column2Header ?? null,
                nameAr: subject.nameAr ?? ''
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
                this.revealCycleFor(this.editing.level);
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
