/**
 * Écran Classes (`classroomsView`, wwwroot/js/classrooms.js) — champ « Série » (Évolution N°4).
 *
 * Le PUT d'une classe est un remplacement complet : une série omise est EFFACÉE côté serveur. Ces
 * tests verrouillent donc que la série est toujours renvoyée à la modification, qu'elle n'est jamais
 * envoyée vide ('' ne se lit pas comme « aucune série ») et qu'elle disparaît quand la classe quitte le lycée.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const CATALOG = [{ code: 'S1', label: 'Série S1' }, { code: 'S2', label: 'Série S2' }];

async function classroomsView({ role = 'Directeur', catalogFails = false } = {}) {
    const calls = [];
    const ctx = loadScripts(['classrooms.js'], {
        preload: {
            auth: { role },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/coefficients/catalog') {
                        if (catalogFails) throw new Error('403');
                        return CATALOG;
                    }
                    return [];
                },
                post: async (endpoint, body) => { calls.push({ method: 'POST', endpoint, body: plain(body) }); return {}; },
                put: async (endpoint, body) => { calls.push({ method: 'PUT', endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/classes', search: '' }
        }
    });
    const view = ctx.component('classroomsView');
    await flush();
    return { view, calls };
}

const lyceeClass = (series) => ({
    id: 'c-1', name: 'Terminale S2 A', level: 'Lycée', capacity: 40, rowVersion: 3,
    isAccelerated: false, targetLevel: null, series
});

test('le catalogue des séries vient de l\'API, pas d\'une copie locale', async () => {
    const { view, calls } = await classroomsView();

    assert.ok(calls.some((c) => c.endpoint === '/coefficients/catalog'));
    assert.deepEqual(plain(view.seriesOptions.map((o) => o.value)), ['', 'S1', 'S2']);
});

test('le champ « Série » n\'apparaît que pour un lycée, et seulement si le catalogue est connu', async () => {
    const { view } = await classroomsView();

    assert.equal(view.showSeriesField({ level: 'Lycée' }), true);
    assert.equal(view.showSeriesField({ level: 'lycee' }), true, 'casse et accents ignorés comme côté serveur');
    assert.equal(view.showSeriesField({ level: 'Secondaire' }), true);
    assert.equal(view.showSeriesField({ level: 'Collège' }), false);
    assert.equal(view.showSeriesField({ level: 'Primaire' }), false);

    const { view: withoutCatalog } = await classroomsView({ catalogFails: true });
    assert.equal(withoutCatalog.showSeriesField({ level: 'Lycée' }), false, 'catalogue indisponible : champ masqué, classe enregistrable sans série');
});

test('un rôle qui ne gère pas les classes n\'appelle pas le catalogue', async () => {
    const { calls } = await classroomsView({ role: 'Enseignant' });

    assert.equal(calls.some((c) => c.endpoint === '/coefficients/catalog'), false);
});

test('modifier une classe renvoie TOUJOURS sa série (le PUT remplace tout)', async () => {
    const { view, calls } = await classroomsView();

    view.openEdit(lyceeClass('S2'));
    assert.equal(view.editing.series, 'S2');
    await view.submitEdit();

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.body.series, 'S2');
});

test('sans série choisie, l\'API reçoit null et jamais une chaîne vide', async () => {
    const { view, calls } = await classroomsView();

    view.openEdit(lyceeClass(null));
    await view.submitEdit();
    assert.strictEqual(calls.find((c) => c.method === 'PUT').body.series, null);

    view.newClassroom.name = 'Seconde A';
    view.newClassroom.level = 'Lycée';
    await view.submitCreate();
    assert.strictEqual(calls.find((c) => c.method === 'POST').body.series, null);
});

test('la création envoie la série choisie', async () => {
    const { view, calls } = await classroomsView();

    view.newClassroom.name = 'Terminale S1 A';
    view.newClassroom.level = 'Lycée';
    view.newClassroom.series = 'S1';
    await view.submitCreate();

    assert.equal(calls.find((c) => c.method === 'POST').body.series, 'S1');
});

test('passer une classe du lycée à un autre cycle efface sa série', async () => {
    const { view, calls } = await classroomsView();

    view.openEdit(lyceeClass('S2'));
    view.editing.level = 'Collège';
    view.onLevelChanged(view.editing);
    assert.equal(view.editing.series, '');

    await view.submitEdit();
    assert.strictEqual(calls.find((c) => c.method === 'PUT').body.series, null);
});
