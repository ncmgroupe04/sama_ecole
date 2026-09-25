/**
 * Rapport d'assiduité (Évolution N°5) — onglet « Par matière » et infobulle des séances appelées.
 *
 *   1. « Par matière » interroge GET /reports/attendance/by-subject avec les MÊMES filtres (période, classe).
 *   2. Le rapport par élève reste chargé comme avant ; changer de filtre recharge l'onglet actif.
 *   3. Une erreur serveur n'échoue pas en silence.
 *   4. L'infobulle donne le dénominateur : le nombre de séances appelées.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

const BY_SUBJECT = [
    { subjectId: 'maths', subjectName: 'Mathématiques', sessions: 12, lines: 240, presents: 200, lates: 10, justifiedAbsences: 12, unjustifiedAbsences: 18, attendanceRate: 0.875 }
];

async function boot({ subjects = BY_SUBJECT, failSubjects = null } = {}) {
    const calls = { get: [], toasts: [] };
    const api = {
        get: async (endpoint) => {
            calls.get.push(endpoint);
            if (endpoint.startsWith('/reports/attendance/by-subject')) {
                if (failSubjects) throw failSubjects;
                return subjects;
            }
            if (endpoint.startsWith('/reports/attendance')) return { students: [], totalCount: 0, averageAttendanceRate: null };
            return [];
        },
        toMessage: (e, fallback) => (e && e.message) || fallback
    };
    const ctx = loadScripts(['auth.js', 'pdf-preview.js', 'attendance-report.js'], { preload: { api } });
    ctx.sandbox.toast = { error: (m) => calls.toasts.push(m), success() {} };
    const view = ctx.component('attendanceReportView');
    await flush();
    return { view, calls };
}

test('le rapport s\'ouvre sur la vue par élève, sans requête par matière', async () => {
    const { view, calls } = await boot();

    assert.equal(view.tab, 'student');
    assert.equal(calls.get.some((u) => u.startsWith('/reports/attendance/by-subject')), false);
});

test('l\'onglet « Par matière » charge la vue par matière avec la période et la classe du filtre', async () => {
    const { view, calls } = await boot();
    view.classId = 'c1';
    view.startDate = '2026-09-01';
    view.endDate = '2026-09-30';

    view.setTab('subject');
    await flush();

    const request = calls.get.find((u) => u.startsWith('/reports/attendance/by-subject'));
    assert.ok(request.includes('startDate=2026-09-01'));
    assert.ok(request.includes('endDate=2026-09-30'));
    assert.ok(request.includes('classId=c1'));
    assert.equal(view.subjects.length, 1);
    assert.equal(view.subjects[0].subjectName, 'Mathématiques');
});

test('sans classe choisie, aucun classId n\'est envoyé (toutes les classes)', async () => {
    const { view, calls } = await boot();

    view.setTab('subject');
    await flush();

    assert.equal(calls.get.find((u) => u.startsWith('/reports/attendance/by-subject')).includes('classId'), false);
});

test('changer de filtre recharge la vue par matière quand elle est affichée', async () => {
    const { view, calls } = await boot();
    view.setTab('subject');
    await flush();
    const before = calls.get.filter((u) => u.startsWith('/reports/attendance/by-subject')).length;

    view.applyFilters();
    await flush();

    assert.equal(calls.get.filter((u) => u.startsWith('/reports/attendance/by-subject')).length, before + 1);
});

test('changer de filtre depuis la vue par élève ne charge pas la vue par matière', async () => {
    const { view, calls } = await boot();

    view.applyFilters();
    await flush();

    assert.equal(calls.get.some((u) => u.startsWith('/reports/attendance/by-subject')), false);
});

test('une erreur serveur sur la vue par matière est signalée', async () => {
    const { view, calls } = await boot({ failSubjects: new Error('Accès refusé.') });

    view.setTab('subject');
    await flush();

    assert.equal(view.subjects.length, 0);
    assert.equal(view.subjectsError, 'Accès refusé.');
    assert.deepEqual(calls.toasts, ['Accès refusé.']);
});

test('l\'infobulle donne le nombre de séances appelées et de jours avec appel', async () => {
    const { view } = await boot();

    assert.equal(view.sessionsTooltip({ totalCalls: 12, daysRecorded: 5 }), '12 séances appelées · 5 jours avec appel');
    assert.equal(view.sessionsTooltip({ totalCalls: 1, daysRecorded: 1 }), '1 séance appelée · 1 jour avec appel');
    assert.equal(view.sessionsTooltip({}), '0 séance appelée · 0 jour avec appel');
});
