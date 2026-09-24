/**
 * Onglet « Coefficients » de l'écran Matières (`coefficientsView`, wwwroot/js/coefficients.js) —
 * Évolution N°4.
 *
 * Le composant n'a aucune règle métier : il affiche le coefficient EFFECTIF et l'ORIGINE calculés par
 * le serveur. Ces tests verrouillent ce qu'il envoie (portée, jeton xmin, jamais un champ vide), ce
 * qu'il refuse d'offrir (classes du primaire, modèle national pour une classe, écriture au Secrétariat)
 * et la façon dont il réagit à un conflit 409.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const CATALOG = [
    { code: 'L1', label: 'Série L1' },
    { code: 'L2', label: 'Série L2' },
    { code: 'S2', label: 'Série S2' }
];

const CLASSROOMS = [
    { id: 'c-cm2', name: 'CM2 A', cycle: 'Primaire' },
    { id: 'c-6a', name: '6e A', cycle: 'College' },
    { id: 'c-ts2', name: 'Terminale S2 A', cycle: 'Lycee' },
    { id: 'c-mat', name: 'Grande section', cycle: 'Maternelle' }
];

const YEARS = [
    { id: 'y-2025', label: '2025-2026', endDate: '2026-07-31', isActive: true },
    { id: 'y-2024', label: '2024-2025', endDate: '2025-07-31', isActive: false },
    { id: 'y-2023', label: '2023-2024', endDate: '2024-07-31', isActive: false }
];

const gridRow = (overrides = {}) => ({
    subjectId: 's-maths', subjectName: 'Mathématiques', level: 'Lycée',
    baseCoefficient: 4, overrideId: null, overrideCoefficient: null, rowVersion: null,
    inheritedSeriesCoefficient: null, effectiveCoefficient: 4, source: 'Subject',
    ...overrides
});

/**
 * Charge coefficients.js avec une API factice qui enregistre chaque appel. `grid` est une fonction :
 * elle reçoit l'URL demandée et renvoie la grille (ou lève) — un test peut ainsi faire évoluer la
 * réponse d'un appel à l'autre.
 */
async function boot({ role = 'Directeur', grid, failWith = {} } = {}) {
    const calls = [];
    const record = (method) => async (endpoint, body) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        if (failWith[method]) throw failWith[method];
        return null;
    };

    const ctx = loadScripts(['coefficients.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/coefficients/catalog') return CATALOG;
                    if (endpoint === '/classrooms') return CLASSROOMS;
                    if (endpoint === '/school-years') return YEARS;
                    if (endpoint.startsWith('/coefficients?')) {
                        return grid ? grid(endpoint) : { schoolYearId: 'y-2025', schoolYearLabel: '2025-2026', yearHasGrades: false, rows: [gridRow()] };
                    }
                    return [];
                },
                put: record('PUT'),
                post: async (endpoint, body) => {
                    calls.push({ method: 'POST', endpoint, body: plain(body) });
                    if (failWith.POST) throw failWith.POST;
                    if (endpoint === '/coefficients/apply-template') {
                        return { applied: 5, updated: 0, skippedExisting: 2, unmatchedTemplateLines: ['LV2'], uncoveredSubjects: ['Dessin'] };
                    }
                    return { copied: 3, skipped: 1 };
                },
                delete: record('DELETE'),
                toMessage: (e, fallback) => (e && e.message) || fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    const view = ctx.component('coefficientsView');
    await flush();
    return { view, calls };
}

const conflict = () => Object.assign(new Error('conflit'), { status: 409, code: 'CONCURRENCY_CONFLICT' });

test('au chargement : la première série du catalogue est chargée et remplit la grille', async () => {
    const { view, calls } = await boot();

    assert.equal(view.series, 'L1');
    assert.ok(calls.some((c) => c.endpoint === '/coefficients?series=L1'));
    assert.equal(view.rows.length, 1);
    assert.equal(view.rows[0].name, 'Mathématiques');
    assert.equal(view.rows[0].effective, 4);
    assert.equal(view.yearLabel, '2025-2026');
});

test('la grille affiche l\'effectif et l\'origine du SERVEUR, sans les recalculer', async () => {
    const { view } = await boot({
        grid: () => ({
            schoolYearId: 'y', schoolYearLabel: '2025-2026', yearHasGrades: false,
            rows: [gridRow({ overrideId: 'o-1', overrideCoefficient: 6, rowVersion: 42, effectiveCoefficient: 6, source: 'Series' })]
        })
    });

    assert.equal(view.rows[0].effective, 6);
    assert.equal(view.sourceLabel(view.rows[0].source), 'Série');
    assert.equal(view.rows[0].draft, '6', 'le champ est prérempli avec la surcharge enregistrée');
});

test('save (série) envoie {subjectId, series, coefficient} sans rowVersion pour une création', async () => {
    const { view, calls } = await boot();
    view.selectSeries('S2');
    await flush();

    view.rows[0].draft = '6,5'; // clavier français
    await view.save(view.rows[0]);

    const put = calls.find((c) => c.method === 'PUT');
    assert.deepEqual(put.body, { subjectId: 's-maths', coefficient: 6.5, series: 'S2' });
    assert.equal(put.endpoint, '/coefficients');
});

test('save d\'une ligne existante joint le jeton rowVersion (verrou optimiste)', async () => {
    const { view, calls } = await boot({
        grid: () => ({
            schoolYearId: 'y', schoolYearLabel: '2025-2026', yearHasGrades: false,
            rows: [gridRow({ overrideId: 'o-1', overrideCoefficient: 6, rowVersion: 42, effectiveCoefficient: 6, source: 'Series' })]
        })
    });

    view.rows[0].draft = '7';
    await view.save(view.rows[0]);

    const put = calls.find((c) => c.method === 'PUT');
    assert.deepEqual(put.body, { subjectId: 's-maths', coefficient: 7, series: 'L1', rowVersion: 42 });
});

test('un champ vide ou illisible n\'envoie AUCUNE requête (jamais "" vers le serveur)', async () => {
    const { view, calls } = await boot();

    view.rows[0].draft = '';
    await view.save(view.rows[0]);
    view.rows[0].draft = 'abc';
    await view.save(view.rows[0]);

    assert.equal(calls.filter((c) => c.method === 'PUT').length, 0);
    assert.match(view.rows[0].error, /coefficient/i);
    assert.equal(view.canSave(view.rows[0]), false);
});

test('une réponse 409 affiche « modifié entre-temps » et recharge la grille', async () => {
    const { view, calls } = await boot({ failWith: { PUT: conflict() } });
    const gridLoadsBefore = calls.filter((c) => c.endpoint.startsWith('/coefficients?')).length;

    view.rows[0].draft = '9';
    await view.save(view.rows[0]);

    assert.match(view.error, /modifié entre-temps/);
    const gridLoadsAfter = calls.filter((c) => c.endpoint.startsWith('/coefficients?')).length;
    assert.equal(gridLoadsAfter, gridLoadsBefore + 1, 'la grille est rechargée pour montrer l\'état réel');
});

test('« Rétablir » appelle DELETE avec le jeton puis recharge', async () => {
    const { view, calls } = await boot({
        grid: () => ({
            schoolYearId: 'y', schoolYearLabel: '2025-2026', yearHasGrades: false,
            rows: [gridRow({ overrideId: 'o-9', overrideCoefficient: 6, rowVersion: 7, effectiveCoefficient: 6, source: 'Series' })]
        })
    });
    const gridLoadsBefore = calls.filter((c) => c.endpoint.startsWith('/coefficients?')).length;

    await view.restore(view.rows[0]);

    const del = calls.find((c) => c.method === 'DELETE');
    assert.equal(del.endpoint, '/coefficients/o-9?rowVersion=7');
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/coefficients?')).length, gridLoadsBefore + 1);
});

test('le sélecteur « Classe » n\'offre que les classes de Collège et de Lycée', async () => {
    const { view } = await boot();

    assert.deepEqual(plain(view.classroomChoices.map((c) => c.label)), ['6e A', 'Terminale S2 A']);
});

test('portée « classe » : la grille se charge par classroomId et save n\'envoie pas de série', async () => {
    const { view, calls } = await boot();
    view.setScope('classroom');
    await flush();
    assert.equal(view.rows.length, 0, 'aucune requête de grille tant qu\'aucune classe n\'est choisie');

    view.selectClassroom('c-ts2');
    await flush();
    assert.ok(calls.some((c) => c.endpoint === '/coefficients?classroomId=c-ts2'));

    view.rows[0].draft = '8';
    await view.save(view.rows[0]);
    const put = calls.find((c) => c.method === 'PUT');
    assert.deepEqual(put.body, { subjectId: 's-maths', coefficient: 8, classroomId: 'c-ts2' });
});

test('« Appliquer le modèle » est indisponible pour la portée « classe »', async () => {
    const { view } = await boot();
    assert.equal(view.canApplyTemplate, true);

    view.setScope('classroom');
    assert.equal(view.canApplyTemplate, false);
});

test('appliquer le modèle envoie la série, garde le rapport et recharge la grille', async () => {
    const { view, calls } = await boot();

    await view.applyTemplate(false);

    const post = calls.find((c) => c.endpoint === '/coefficients/apply-template');
    assert.deepEqual(post.body, { series: 'L1', overwrite: false });
    assert.equal(view.templateReport.applied, 5);
    assert.equal(view.templateReport.skippedExisting, 2);
    assert.deepEqual(plain(view.templateReport.unmatched), ['LV2']);
    assert.deepEqual(plain(view.templateReport.uncovered), ['Dessin']);
});

test('« Reprendre l\'année précédente » cible l\'année terminée la plus récente', async () => {
    const { view, calls } = await boot();
    assert.equal(view.previousYear.id, 'y-2024');

    await view.carryOver();

    const post = calls.find((c) => c.endpoint === '/coefficients/carry-over');
    assert.deepEqual(post.body, { fromSchoolYearId: 'y-2024' });
    assert.match(view.notice, /3 coefficient/);
});

test('le Secrétariat lit la grille mais ne peut ni écrire, ni appliquer, ni reprendre', async () => {
    const { view } = await boot({ role: 'Secretariat' });

    assert.equal(view.canEdit, false);
    assert.equal(view.rows.length, 1, 'la grille reste lisible');
    view.rows[0].draft = '9';
    assert.equal(view.canSave(view.rows[0]), false);
    assert.equal(view.canApplyTemplate, false);
    assert.equal(view.canCarryOver, false);
});

test('l\'avertissement rétroactif s\'appuie sur yearHasGrades reçu du serveur', async () => {
    const { view } = await boot({
        grid: () => ({ schoolYearId: 'y', schoolYearLabel: '2025-2026', yearHasGrades: true, rows: [gridRow()] })
    });

    assert.equal(view.yearHasGrades, true);
});
