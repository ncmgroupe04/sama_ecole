/**
 * Programmes et cahier de texte (Évolution N°7) : `syllabusView` (wwwroot/js/syllabus.js) et le pointage des chapitres
 * dans `classJournalView` (wwwroot/js/class-journal.js). Aucun des deux écrans ne calcule d'avancement : ils affichent
 * ce que renvoie le serveur et lui transmettent les chapitres cochés.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const COVERAGE = {
    schoolYearLabel: '2026-2027',
    rows: [
        { classroomId: 'c3a', classroomName: '3e A', gradeLevel: 'Troisième', subjectId: 'maths', subjectName: 'Mathématiques', teachers: ['M. Diop'], coveredUnits: 3, totalUnits: 11, percent: 27.3, lastSessionDate: '2026-11-04' },
        { classroomId: 'c3b', classroomName: '3e B', gradeLevel: 'Troisième', subjectId: 'maths', subjectName: 'Mathématiques', teachers: [], coveredUnits: 0, totalUnits: 11, percent: 0, lastSessionDate: null }
    ],
    bySubject: [{ subjectName: 'Mathématiques', gradeLevel: 'Troisième', classes: 2, averagePercent: 13.6 }],
    byTeacher: [{ teacherName: 'M. Diop', classes: 1, averagePercent: 27.3 }]
};

const UNITS = {
    gradeLevel: 'Troisième',
    hasTemplate: true,
    units: [
        { id: 'u1', subjectId: 'maths', gradeLevel: 'Troisième', section: 'Activités numériques', title: 'Racine carrée', order: 1, plannedHours: 8, rowVersion: 11 },
        { id: 'u2', subjectId: 'maths', gradeLevel: 'Troisième', section: 'Activités numériques', title: 'Calcul algébrique', order: 2, plannedHours: null, rowVersion: 12 },
        { id: 'u3', subjectId: 'maths', gradeLevel: 'Troisième', section: 'Activités géométriques', title: 'Théorème de Thalès', order: 3, plannedHours: 10, rowVersion: 13 }
    ]
};

function boot(role = 'Directeur') {
    const calls = [];
    const ctx = loadScripts(['syllabus.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/syllabus/coverage') return COVERAGE;
                    if (endpoint === '/subjects') return [{ id: 'maths', name: 'Mathématiques', level: 'Collège' }];
                    if (endpoint.startsWith('/syllabus/units')) return UNITS;
                    return [];
                },
                post: async (endpoint, body) => { calls.push({ method: 'POST', endpoint, body: plain(body) }); return { added: 2 }; },
                put: async (endpoint, body) => { calls.push({ method: 'PUT', endpoint, body: plain(body) }); return null; },
                delete: async (endpoint) => { calls.push({ method: 'DELETE', endpoint }); return null; },
                toMessage: (_e, fallback) => fallback
            }
        }
    });
    return { view: ctx.component('syllabusView'), calls };
}

test("l'avancement de l'année active est chargé à l'ouverture, avec les moyennes par matière et par enseignant", async () => {
    const { view, calls } = boot('Secretariat');
    await flush();

    assert.ok(calls.some((c) => c.endpoint === '/syllabus/coverage'));
    assert.equal(view.coverageRows.length, 2);
    assert.equal(view.coverage.byTeacher[0].teacherName, 'M. Diop');
    assert.equal(view.percentLabel(27.3), '27,3 %');
    assert.equal(view.percentLabel(null), '—', 'un programme vide ne se lit jamais « 0 % »');
});

test('le filtre par classe ne garde que ses lignes', async () => {
    const { view } = boot();
    await flush();

    view.coverageFilter = '3e B';
    assert.deepEqual(plain(view.coverageRows.map((r) => r.classroomName)), ['3e B']);
});

test('« Programme » depuis une ligne ouvre le référentiel de la matière pour le niveau de la classe', async () => {
    const { view, calls } = boot();
    await flush();

    view.openProgramme(COVERAGE.rows[0]);
    await flush();

    assert.equal(view.tab, 'units');
    assert.ok(calls.some((c) => c.endpoint === '/syllabus/units?subjectId=maths&gradeLevel=Troisi%C3%A8me'));
    assert.equal(view.rows.length, 3);
});

test('un chapitre par ligne : les lignes vides sont ignorées', async () => {
    const { view, calls } = boot();
    await flush();
    view.subjectId = 'maths';
    view.gradeLevel = 'Troisième';
    await view.loadUnits();

    view.addForm.section = 'Activités numériques';
    view.addForm.titlesText = 'Statistiques\n\n  Équations  \n';
    await view.submitAdd();

    const post = calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/syllabus/units');
    assert.deepEqual(post.body, { subjectId: 'maths', gradeLevel: 'Troisième', section: 'Activités numériques', titles: ['Statistiques', 'Équations'] });
});

test('la trame nationale ne s\'importe que si le serveur en connaît une, et par le Directeur seul', async () => {
    const { view, calls } = boot();
    await flush();
    view.subjectId = 'maths';
    view.gradeLevel = 'Troisième';
    await view.loadUnits();

    assert.equal(view.programme.hasTemplate, true);
    await view.importTemplate();
    assert.deepEqual(calls.find((c) => c.method === 'POST').body, { subjectId: 'maths', gradeLevel: 'Troisième' });

    assert.equal(boot('Secretariat').view.canEdit, false);
});

test('un chapitre corrigé renvoie son rowVersion et un volume horaire saisi à la française', async () => {
    const { view, calls } = boot();
    await flush();
    view.subjectId = 'maths';
    view.gradeLevel = 'Troisième';
    await view.loadUnits();

    const row = view.rows[1];
    assert.equal(view.isDirty(row), false);
    row.draftHours = '6,5';
    assert.equal(view.isDirty(row), true);
    await view.saveUnit(row);

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/syllabus/units/u2');
    assert.deepEqual(put.body, { title: 'Calcul algébrique', section: 'Activités numériques', order: 2, plannedHours: 6.5, rowVersion: 12 });
});

test('un volume horaire illisible est refusé avant tout appel', async () => {
    const { view, calls } = boot();
    await flush();
    view.subjectId = 'maths';
    view.gradeLevel = 'Troisième';
    await view.loadUnits();

    const row = view.rows[0];
    row.draftHours = 'huit';
    await view.saveUnit(row);
    assert.ok(row.error);
    assert.equal(calls.filter((c) => c.method === 'PUT').length, 0);
});

test('retirer un chapitre se confirme en deux temps', async () => {
    const { view, calls } = boot();
    await flush();
    view.subjectId = 'maths';
    view.gradeLevel = 'Troisième';
    await view.loadUnits();

    const row = view.rows[0];
    view.archive(row);
    assert.equal(calls.filter((c) => c.method === 'DELETE').length, 0, 'premier clic : confirmation seulement');
    await view.archive(row);
    await flush();
    assert.ok(calls.some((c) => c.method === 'DELETE' && c.endpoint === '/syllabus/units/u1?rowVersion=11'));
});

// ------------------------------------------------------------------------------------------ Cahier de texte

function bootJournal() {
    const calls = [];
    const ctx = loadScripts(['class-journal.js'], {
        preload: {
            auth: { role: 'Enseignant' },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint.startsWith('/syllabus/units')) return UNITS;
                    if (endpoint.startsWith('/class-journal')) return { items: [], totalCount: 0 };
                    return [];
                },
                post: async (endpoint, body) => { calls.push({ method: 'POST', endpoint, body: plain(body) }); return {}; },
                put: async (endpoint, body) => { calls.push({ method: 'PUT', endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    ctx.sandbox.toast = { success() {}, error() {} };
    const view = ctx.component('classJournalView');
    view.$nextTick = (fn) => fn();
    view.$refs = {};
    return { view, calls };
}

test('le programme de la matière pour la classe s\'affiche dès que les deux sont choisies, groupé par partie', async () => {
    const { view, calls } = bootJournal();
    await flush();

    view.openCreate();
    view.newEntry.classroomId = 'c3a';
    view.onCreateTargetChange();
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/syllabus/units')).length, 0, 'sans matière, aucun appel');

    view.newEntry.subjectId = 'maths';
    view.onCreateTargetChange();
    await flush();

    assert.ok(calls.some((c) => c.endpoint === '/syllabus/units?subjectId=maths&classroomId=c3a'));
    assert.deepEqual(plain(view.programmeSections.map((s) => [s.name, s.units.length])),
        [['Activités numériques', 2], ['Activités géométriques', 1]]);
});

test('les chapitres cochés partent avec la séance ; changer de matière les décoche', async () => {
    const { view, calls } = bootJournal();
    await flush();

    view.openCreate();
    Object.assign(view.newEntry, { classroomId: 'c3a', subjectId: 'maths', sessionDate: '2026-11-04', topic: 'Racines', content: 'Cours' });
    view.onCreateTargetChange();
    await flush();

    view.toggleUnit(view.newEntry.syllabusUnitIds, 'u1');
    view.toggleUnit(view.newEntry.syllabusUnitIds, 'u3');
    view.toggleUnit(view.newEntry.syllabusUnitIds, 'u3');
    await view.submitCreate();

    const post = calls.find((c) => c.method === 'POST');
    assert.deepEqual(post.body.syllabusUnitIds, ['u1']);

    view.openCreate();
    view.newEntry.syllabusUnitIds.push('u1');
    view.onCreateTargetChange();
    assert.deepEqual(plain(view.newEntry.syllabusUnitIds), []);
});

test('corriger une séance repart de ses chapitres déjà pointés', async () => {
    const { view, calls } = bootJournal();
    await flush();

    view.openEdit({ id: 'e1', classroomId: 'c3a', subjectId: 'maths', topic: 'T', content: 'C', homework: null, homeworkDueDate: null, rowVersion: 5, syllabusUnitIds: ['u2'] });
    await flush();
    view.toggleUnit(view.editingEntry.syllabusUnitIds, 'u1');
    await view.submitEdit();

    const put = calls.find((c) => c.method === 'PUT');
    assert.deepEqual(put.body.syllabusUnitIds, ['u2', 'u1']);
    assert.equal(put.body.rowVersion, 5);
});
