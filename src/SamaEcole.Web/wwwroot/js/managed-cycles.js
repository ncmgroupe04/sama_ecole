/*
 * Cycles gérés par l'établissement (SchoolSettings.ManagedCycles) — règles PURES partagées par le store
 * schoolConfig (auth.js), l'écran des classes (classrooms.js), l'inscription (enrollments.js) et les examens
 * (exams.js). Aucun accès à Alpine ni à l'API : tout se teste seul (tests/js/managed-cycles.test.mjs).
 *
 * Les cycles sont les noms de CycleType renvoyés par l'API : Maternelle, Primaire, College, Lycee. Il n'y a PAS
 * de cycle « Crèche » : Crèche et Maternelle sont deux NIVEAUX du cycle Maternelle.
 *
 * CONFORT d'affichage : le serveur ne refuse pas la création d'une classe hors des cycles gérés. Ces règles ne
 * servent qu'à ne pas encombrer les listes — et jamais à faire disparaître une donnée existante.
 */
(() => {
    /** Tous les cycles, dans l'ordre canonique. */
    const ALL = ['Maternelle', 'Primaire', 'College', 'Lycee'];

    /** Niveau (libellé de l'écran, accents et casse ignorés) → cycle. Un niveau inconnu n'a pas de cycle (null). */
    const CYCLE_BY_LEVEL = {
        creche: 'Maternelle',
        maternelle: 'Maternelle',
        primaire: 'Primaire',
        college: 'College',
        lycee: 'Lycee'
    };

    const stripAccents = (text) => String(text || '').normalize('NFD').replace(/[̀-ͯ]/g, '');

    /**
     * Liste reçue de l'API → liste utilisable, dans l'ordre canonique. Une valeur absente, vide, ou qui ne
     * contient AUCUN cycle connu retombe sur TOUS les cycles — on n'affiche jamais moins que ce que l'école avait
     * (réponse partielle, API plus ancienne, erreur de chargement). Les noms inconnus sont ignorés.
     */
    function normalize(names) {
        if (!Array.isArray(names)) return [...ALL];

        const known = ALL.filter((cycle) => names.includes(cycle));
        return known.length > 0 ? known : [...ALL];
    }

    /** Cycle d'un niveau (« Crèche » → Maternelle, « Collège » → College), ou null s'il est inconnu. */
    function cycleOfLevel(level) {
        return CYCLE_BY_LEVEL[stripAccents(level).trim().toLowerCase()] || null;
    }

    /**
     * Vrai si le cycle est géré. Un cycle inconnu ou absent (null) est considéré comme géré : on ne cache
     * jamais ce qu'on ne sait pas classer.
     */
    function isManaged(managed, cycle) {
        return !cycle || !ALL.includes(cycle) || managed.includes(cycle);
    }

    /**
     * Types d'examen officiels proposés : CFEE (fin de Primaire), BFEM (fin de Collège), BAC (fin de Lycée) — la
     * même correspondance que CreateExamDossierCommandHandler.ExpectedCycle côté serveur. Si AUCUN cycle géré ne
     * porte d'examen (ex. Maternelle seule), on garde les trois : une liste vide laisserait un sélecteur sans
     * option.
     */
    function examTypes(managed) {
        const types = [];
        if (managed.includes('Primaire')) types.push('CFEE');
        if (managed.includes('College')) types.push('BFEM');
        if (managed.includes('Lycee')) types.push('BAC');

        return types.length > 0 ? types : ['CFEE', 'BFEM', 'BAC'];
    }

    window.managedCycles = { ALL, normalize, cycleOfLevel, isManaged, examTypes };
})();
