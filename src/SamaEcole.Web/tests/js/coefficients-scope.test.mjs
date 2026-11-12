/**
 * Onglet « Coefficients » (wwwroot/js/coefficients.js) : portée d'une série et fenêtres d'avertissement.
 * Une série ne vaut que pour les classes de LYCÉE qui la portent ; le collège et les classes sans série se
 * règlent en mode « Classe ». L'écran doit le dire (fenêtre) au lieu de laisser croire à un bug.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function view({ puts = [] } = {}) {
    const ctx = loadScripts(['coefficients.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async () => ({ schoolYearLabel: '2026-2027', yearHasGrades: false, rows: [] }),
                put: async (url, body) => { puts.push({ url, body }); return {}; },
                toMessage: (err, fallback) => (err && err.message) || fallback
            }
        }
    });
    const v = ctx.initAlpine().get('coefficientsView')();
    v.classrooms = [
        { id: 'c1', name: 'TS2 A', cycle: 'Lycee', series: 'S2' },
        { id: 'c2', name: 'TL B', cycle: 'Lycee', series: null },
        { id: 'c3', name: '4eme A', cycle: 'College', series: null },
        { id: 'c4', name: 'CM2 A', cycle: 'Primaire', series: null }
    ];
    return v;
}

const row = (overrides = {}) => ({ subjectId: 's1', name: 'Mathématiques', draft: '7', override: null, overrideId: null, isSaving: false, error: null, ...overrides });

test('a series only concerns the lycée classes that carry it (case-insensitive)', () => {
    const v = view();
    assert.deepEqual(v.classesOfSeries('s2').map((c) => c.name), ['TS2 A']);
    assert.deepEqual(v.classesOfSeries('S1'), []);
    assert.deepEqual(v.classesOfSeries(''), []);
});

test('lists the lycée classes without a series and the collège classes separately', () => {
    const v = view();
    assert.deepEqual(v.lyceeWithoutSeries.map((c) => c.name), ['TL B']);
    assert.deepEqual(v.collegeClasses.map((c) => c.name), ['4eme A']);
});

test('saving a series coefficient that reaches no class opens an explanatory warning', () => {
    const v = view();
    v.scope = 'series';
    v.series = 'S1';
    v.explainSaveScope(row());
    assert.equal(v.warning.open, true);
    assert.match(v.warning.title, /Aucune classe n'utilise la série S1/);
    assert.match(v.warning.hint, /TL B/);
});

test('saving a series coefficient that reaches classes names them and opens no warning', () => {
    const v = view();
    v.scope = 'series';
    v.series = 'S2';
    v.explainSaveScope(row());
    assert.equal(v.warning.open, false);
    assert.match(v.notice, /TS2 A/);
});

test('a class coefficient says it overrides the series of that class', () => {
    const v = view();
    v.scope = 'classroom';
    v.classroomId = 'c1';
    v.explainSaveScope(row());
    assert.match(v.notice, /prime sur celui de la série S2/);
});

test('an out-of-range value is refused client-side with a warning and no request', async () => {
    const puts = [];
    const v = view({ puts });
    for (const bad of ['0', '-1', '25']) {
        v.warning.open = false;
        await v.save(row({ draft: bad }));
        assert.equal(v.warning.open, true, `valeur ${bad}`);
        assert.equal(v.warning.title, 'Coefficient non valide');
    }
    assert.equal(puts.length, 0);
});

test('entering the series scope warns once per page load that it only concerns the lycée', () => {
    const v = view();
    v.scope = 'classroom';
    v.series = 'S2';
    v.setScope('series');
    assert.equal(v.warning.open, true);
    assert.match(v.warning.title, /ne concernent que le lycée/);

    v.closeWarning();
    v.scope = 'classroom';
    v.setScope('series');
    assert.equal(v.warning.open, false);
});
