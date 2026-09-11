/**
 * Écran Matières (`subjectsView`, wwwroot/js/subjects.js) — filtre par CYCLE.
 *
 * Les domaines d'apprentissage (Lettres & Langues, Sciences & Technologies…) traversent tous les
 * cycles : sans second axe, l'Anglais du Lycée et la « Langue et Communication » du Primaire se
 * lisent dans la même colonne. Ce fichier verrouille les règles du filtre — le classement d'un
 * niveau dans son cycle, le cycle affiché au premier chargement, et le fait qu'une matière
 * enregistrée reste visible après l'enregistrement.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

let nextId = 0;
const subject = (name, level, coefficient = 1) => ({
    id: `s-${++nextId}`, name, level, coefficient, rowVersion: '1',
    parentSubjectId: null, maxScore: null, displayOrder: 0
});

/** Jeu de référence : une école qui couvre Primaire, Collège et Lycée. */
const THREE_CYCLES = () => [
    subject('Français', 'Primaire'),
    subject('Langue et Communication', 'Primaire'),
    subject('Maths', 'Collège'),
    subject('Anglais', 'Lycée')
];

/**
 * Instancie le composant avec une liste de matières servie par l'API, puis exécute init().
 * `search` reproduit le ?q=/?cycle= de l'URL — l'écran les lit au démarrage (deep-link depuis un
 * badge de matière d'un autre écran).
 */
async function subjectsView(rows, { query = '' } = {}) {
    const ctx = loadScripts(['subjects.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async () => rows,
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: `https://localhost/matieres${query}`, search: query },
            history: { replaceState() {} }
        }
    });
    const view = ctx.component('subjectsView');
    await flush();
    return view;
}

test('un niveau est rangé dans son cycle, Crèche et Maternelle partageant « Préscolaire »', async () => {
    const view = await subjectsView([
        subject('Éveil', 'Crèche'),
        subject('Graphisme', 'Maternelle'),
        subject('Français', 'Primaire'),
        subject('Anglais', 'Lycée')
    ]);

    assert.deepEqual(plain(view.cycleTabs).map((t) => [t.key, t.count]),
        [['primaire', 1], ['lycee', 1], ['prescolaire', 2]]);
});

test('les synonymes du serveur sont reconnus, casse et accents ignorés', async () => {
    // Mêmes équivalences que ClassroomCycle.ByLevel : sans quoi l'écran Classes et l'écran Matières
    // rangeraient « Élémentaire » ou « Moyen » dans deux cycles différents.
    const view = await subjectsView([
        subject('Lecture', 'élémentaire'),
        subject('Maths', 'MOYEN'),
        subject('Philo', 'Secondaire')
    ]);

    assert.deepEqual(plain(view.cycleTabs).map((t) => t.key), ['primaire', 'college', 'lycee']);
});

test('un niveau hors nomenclature obtient l\'onglet de repli plutôt que de disparaître', async () => {
    const view = await subjectsView([subject('Français', 'Primaire'), subject('Éveil', 'CI-CP')]);

    const fallback = view.cycleTabs.find((t) => t.key === 'autres');
    assert.ok(fallback, 'un onglet « Autres niveaux » doit exister');
    assert.equal(fallback.count, 1);
});

test('l\'onglet sélectionné filtre la grille par domaines', async () => {
    const view = await subjectsView(THREE_CYCLES());

    view.selectCycle('primaire');
    assert.deepEqual(plain(view.visibleSubjects).map((s) => s.name), ['Français', 'Langue et Communication']);

    view.selectCycle('lycee');
    assert.deepEqual(plain(view.visibleSubjects).map((s) => s.name), ['Anglais']);
});

test('un domaine vide disparaît sous un cycle, mais reste affiché sur « Tous les cycles »', async () => {
    const view = await subjectsView(THREE_CYCLES());

    view.selectCycle('all');
    assert.equal(view.categoryGroups.length, 4, 'les 4 domaines restent visibles, même vides');

    // « Éveil & Petite Enfance » est vide PAR CONSTRUCTION hors préscolaire : l'afficher au Lycée
    // ne dit rien à personne.
    view.selectCycle('lycee');
    assert.deepEqual(plain(view.categoryGroups).map((g) => g.category), ['Lettres & Langues']);
});

test('le premier cycle couvert est sélectionné au chargement quand l\'école en couvre plusieurs', async () => {
    const view = await subjectsView(THREE_CYCLES());
    assert.equal(view.cycle, 'primaire');
});

test('une école mono-cycle ouvre sur « Tous les cycles » — un filtre à un seul choix n\'en est pas un', async () => {
    const view = await subjectsView([subject('Français', 'Primaire'), subject('Maths', 'Primaire')]);
    assert.equal(view.cycle, 'all');
    assert.equal(view.cycleTabs.length, 1);
});

test('un deep-link ?q= ouvre sur « Tous les cycles » : la matière cherchée peut être de n\'importe quel cycle', async () => {
    // Badge de matière cliqué depuis la liste Enseignants : /matieres?q=Anglais. « Anglais » est au
    // Lycée, alors que le cycle par défaut serait Primaire — un onglet présélectionné le masquerait.
    const view = await subjectsView(THREE_CYCLES(), { query: '?q=Anglais' });

    assert.equal(view.cycle, 'all');
    assert.deepEqual(plain(view.visibleSubjects).map((s) => s.name), ['Anglais']);
});

test('?cycle= dans l\'URL gagne, et un cycle non couvert est ignoré', async () => {
    const wanted = await subjectsView(THREE_CYCLES(), { query: '?cycle=lycee' });
    assert.equal(wanted.cycle, 'lycee');

    const unknown = await subjectsView(THREE_CYCLES(), { query: '?cycle=prescolaire' });
    assert.equal(unknown.cycle, 'primaire', 'un cycle sans matière retombe sur le cycle par défaut');
});

test('les compteurs d\'onglets suivent la recherche, la liste des onglets non', async () => {
    const view = await subjectsView(THREE_CYCLES());
    view.search = 'anglais';

    assert.deepEqual(plain(view.cycleTabs).map((t) => [t.key, t.count]),
        [['primaire', 0], ['college', 0], ['lycee', 1]],
        'la barre garde ses 3 onglets et dit où la recherche aboutit');
});

test('enregistrer une matière d\'un autre cycle bascule l\'onglet dessus', async () => {
    // Sinon : « Matière ajoutée » suivi d'une grille où elle ne figure nulle part — l'utilisateur
    // conclut à un échec silencieux et recrée la matière.
    const view = await subjectsView(THREE_CYCLES());
    assert.equal(view.cycle, 'primaire');

    view.revealCycleFor('Lycée');
    assert.equal(view.cycle, 'lycee');
});

test('sur « Tous les cycles », un enregistrement ne déplace pas l\'onglet', async () => {
    const view = await subjectsView(THREE_CYCLES());
    view.selectCycle('all');

    view.revealCycleFor('Lycée');
    assert.equal(view.cycle, 'all');
});

test('le niveau proposé à la création est cherché dans le cycle affiché', async () => {
    const view = await subjectsView(THREE_CYCLES());
    view.selectCycle('lycee');

    view.openCreate();
    assert.equal(view.newSubject.level, 'Lycée');
});
