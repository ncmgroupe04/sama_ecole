/**
 * Volumes horaires et conformité (Évolution N°7) : `hourNormsPanel` et `timetableCompliance`
 * (wwwroot/js/hour-volumes.js). Aucun calcul d'écart côté client : les deux composants affichent ce que le serveur
 * renvoie et lui transmettent les volumes saisis.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const NORMS = {
    gradeLevel: 'Terminale', series: 'S2', hasTemplate: true, totalHours: 28,
    rows: [
        { subjectId: 'svt', subjectName: 'SVT', optionGroup: null, templateHours: 6, gradeHours: null, schoolHours: null, effectiveHours: 6, overrideId: null, rowVersion: null },
        { subjectId: 'maths', subjectName: 'Mathématiques', optionGroup: null, templateHours: 5, gradeHours: null, schoolHours: 4.5, effectiveHours: 4.5, overrideId: 'o1', rowVersion: 21 },
        { subjectId: null, subjectName: 'Philosophie', optionGroup: null, templateHours: 3, gradeHours: null, schoolHours: null, effectiveHours: 3, overrideId: null, rowVersion: null }
    ]
};

const COMPLIANCE = {
    classes: [{
        classroomId: 'c1', classroomName: 'Tle S2', gradeLevel: 'Terminale', series: 'S2',
        totals: { plannedHours: 26, normHours: 28, difference: -2, status: 'Under' },
        subjects: [
            { subjectId: 'svt', subjectName: 'SVT', optionGroup: null, plannedHours: 4, normHours: 6, difference: -2, status: 'Under' },
            { subjectId: 'maths', subjectName: 'Mathématiques', optionGroup: null, plannedHours: 5, normHours: 5, difference: 0, status: 'Compliant' }
        ],
        conflictCount: 1
    }],
    conflicts: [{
        kind: 'Room', dayOfWeek: 'Monday', resource: 'Salle 12',
        first: { classroomName: 'Tle S2', subjectName: 'SVT', teacherName: 'M. Ndiaye', startTime: '08:00:00', endTime: '10:00:00' },
        second: { classroomName: '1re S1', subjectName: 'PC', teacherName: 'Mme Faye', startTime: '09:00:00', endTime: '11:00:00' }
    }]
};

function boot(role = 'Directeur') {
    const calls = [];
    const ctx = loadScripts(['hour-volumes.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint.startsWith('/hour-volumes/norms')) return NORMS;
                    if (endpoint.startsWith('/hour-volumes/compliance')) return COMPLIANCE;
                    return null;
                },
                put: async (endpoint, body) => { calls.push({ method: 'PUT', endpoint, body: plain(body) }); return null; },
                delete: async (endpoint) => { calls.push({ method: 'DELETE', endpoint }); return null; },
                toMessage: (_e, fallback) => fallback
            }
        }
    });
    const components = ctx.initAlpine();
    return { make: (name) => components.get(name)(), calls };
}

test('la série n\'est proposée et transmise qu\'au lycée', async () => {
    const { make, calls } = boot();
    const panel = make('hourNormsPanel');

    panel.gradeLevel = 'Troisième';
    panel.series = 'S2';
    panel.selectGrade();
    await flush();
    assert.equal(panel.isLycee, false);
    assert.ok(calls.some((c) => c.endpoint === '/hour-volumes/norms?gradeLevel=Troisi%C3%A8me'), 'série retirée hors lycée');

    panel.gradeLevel = 'Terminale';
    panel.series = 'S2';
    await panel.load();
    assert.ok(calls.some((c) => c.endpoint === '/hour-volumes/norms?gradeLevel=Terminale&series=S2'));
});

test('un volume se règle au clavier français, avec le rowVersion du réglage existant', async () => {
    const { make, calls } = boot();
    const panel = make('hourNormsPanel');
    panel.gradeLevel = 'Terminale';
    panel.series = 'S2';
    await panel.load();

    const maths = panel.rows[1];
    assert.equal(maths.draft, '4,5');
    assert.equal(panel.isDirty(maths), false);
    maths.draft = '5,5';
    assert.equal(panel.isDirty(maths), true);
    await panel.save(maths);

    const put = calls.find((c) => c.method === 'PUT');
    assert.deepEqual(put.body, { gradeLevel: 'Terminale', series: 'S2', subjectId: 'maths', weeklyHours: 5.5, rowVersion: 21 });

    const svt = panel.rows[0];
    svt.draft = '7';
    await panel.save(svt);
    assert.equal(calls.filter((c) => c.method === 'PUT')[1].body.rowVersion, null, 'premier réglage : aucun rowVersion');
});

test('vider un volume réglé revient à la référence', async () => {
    const { make, calls } = boot();
    const panel = make('hourNormsPanel');
    panel.gradeLevel = 'Terminale';
    panel.series = 'S2';
    await panel.load();

    const maths = panel.rows[1];
    maths.draft = '';
    await panel.save(maths);
    assert.ok(calls.some((c) => c.method === 'DELETE' && c.endpoint === '/hour-volumes/norms/o1?rowVersion=21'));
});

test('un volume illisible est refusé avant tout appel', async () => {
    const { make, calls } = boot();
    const panel = make('hourNormsPanel');
    panel.gradeLevel = 'Terminale';
    await panel.load();

    panel.rows[0].draft = 'six';
    await panel.save(panel.rows[0]);
    assert.ok(panel.rows[0].error);
    assert.equal(calls.filter((c) => c.method === 'PUT').length, 0);
});

test('seul le Directeur règle les volumes', () => {
    const { make } = boot('Secretariat');
    assert.equal(make('hourNormsPanel').canEdit, false);
});

test('heures lisibles : 2,5 h → « 2 h 30 », écarts signés', () => {
    const { make } = boot();
    const panel = make('hourNormsPanel');
    assert.equal(panel.formatHours(2.5), '2 h 30');
    assert.equal(panel.formatHours(3), '3 h');
    assert.equal(panel.formatHours(null), '—');
    assert.equal(panel.formatDifference(-2), '−2 h');
    assert.equal(panel.formatDifference(1.25), '+1 h 15');
    assert.equal(panel.formatDifference(0), '0');
});

test('la conformité se charge pour la classe choisie et se recharge quand la grille change', async () => {
    const { make, calls } = boot();
    const card = make('timetableCompliance');

    const slots = [];
    card.sync('c1', slots);
    await flush();
    assert.equal(calls.filter((c) => c.endpoint === '/hour-volumes/compliance?classroomId=c1').length, 1);
    assert.equal(card.issues, 1, 'une matière sous le volume');
    assert.equal(card.conflicts.length, 1);
    assert.equal(card.conflictLabel(card.conflicts[0].kind), 'Salle');
    assert.equal(card.dayLabel('Monday'), 'Lundi');
    assert.equal(card.slotLabel(card.conflicts[0].first), 'Tle S2 · SVT · M. Ndiaye (08:00–10:00)');

    card.sync('c1', slots);
    await flush();
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/hour-volumes/compliance')).length, 1, 'même classe, même grille : aucun appel');

    card.sync('c1', []);
    await flush();
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/hour-volumes/compliance')).length, 2, 'grille rechargée : nouveau contrôle');

    card.sync('', []);
    await flush();
    assert.equal(card.compliance, null, 'vue « Par enseignant » : rien à contrôler');
});

test('l\'Enseignant ne déclenche aucun contrôle de conformité', async () => {
    const { make, calls } = boot('Enseignant');
    const card = make('timetableCompliance');
    card.sync('c1', []);
    await flush();
    assert.equal(card.visible, false);
    assert.equal(calls.length, 0);
});
