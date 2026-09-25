/**
 * Appel en classe à TROIS statuts (Complément N°5 bis, Tâche 11) — côté navigateur.
 *
 *   1. La liste ne propose que Présent, Absent (justifié) et Absent (non justifié) : plus de « Retard ».
 *   2. Aucune saisie de minutes de retard : les lignes saisies par l'enseignant partent avec `lateMinutes = 0`.
 *   3. Une ligne issue d'un billet d'entrée est VERROUILLÉE (pastille en lecture seule) et repart telle quelle.
 *   4. Un retard historique sans billet est lui aussi affiché en lecture seule (aucune option ne le porte plus).
 *   5. Un élève en retard (billet) compte parmi les présents, comme dans le taux de présence.
 *
 * Confort d'affichage : le serveur refuse tout retard sans billet (422) — voir AttendanceBySlotTests.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

const SLOTS = [
    { slotId: 's1', subjectId: 'maths', subjectName: 'Mathématiques', teacherName: 'Awa Sow', start: '08:00:00', end: '10:00:00', label: '08:00-10:00', sheetId: null }
];

const ROSTER = () => ({
    alreadySubmitted: false,
    students: [
        { studentId: 'awa', matricule: 'E1', fullName: 'Awa Fall', status: 'Late', lateMinutes: 12,
          entryTicketId: 'tk1', entryTicketNumber: 'BILLET-AAAA1111', entryTicketStatus: 'Issued' },
        { studentId: 'ibra', matricule: 'E3', fullName: 'Ibra Ndiaye', status: 'JustifiedAbsence', lateMinutes: 0,
          entryTicketId: 'tk2', entryTicketNumber: 'BILLET-BBBB2222', entryTicketStatus: 'Issued' },
        { studentId: 'modou', matricule: 'E2', fullName: 'Modou Diop', status: null, lateMinutes: 0 }
    ]
});

async function boot({ roster = ROSTER } = {}) {
    const calls = { post: [] };
    const ctx = loadScripts(['auth.js', 'attendance.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') return { workingDays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] };
                    if (endpoint.startsWith('/attendance/slots')) return SLOTS;
                    if (endpoint.startsWith('/attendance/roster')) return roster();
                    if (endpoint === '/classrooms') return [{ id: 'c1', name: 'CM2 A', level: 'Primaire' }];
                    if (endpoint === '/subjects') return [{ id: 'maths', name: 'Mathématiques', level: 'Primaire' }];
                    return [];
                },
                post: async () => ({}),
                postWithRetry: async (endpoint, body) => { calls.post.push({ endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback
            },
            location: { href: 'https://localhost/presences', search: '' }
        }
    });
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role: 'Directeur' }), expiresIn: 900 });
    await ctx.store('schoolConfig').init();
    const view = ctx.component('attendanceView');
    await flush();

    // Un samedi (jour ouvré par défaut) : le test ne dépend pas du jour où il tourne.
    view.filters.date = '2026-09-26';
    view.filters.classroomId = 'c1';
    await view.loadSlots();
    await view.selectSlot(view.slots[0]);
    return { view, calls };
}

test('la liste des statuts ne propose que Présent et les deux absences', async () => {
    const { view } = await boot();

    assert.deepEqual(plain(view.statusOptions.map((o) => o.value)), ['Present', 'JustifiedAbsence', 'UnjustifiedAbsence']);
    assert.deepEqual(plain(view.statusOptions.map((o) => o.label)), ['Présent', 'Absent (justifié)', 'Absent (non justifié)']);
});

test('une ligne issue d\'un billet est verrouillée, les autres restent modifiables', async () => {
    const { view } = await boot();

    const byId = Object.fromEntries(view.entries.map((e) => [e.studentId, e]));
    assert.equal(byId.awa.locked, true);
    assert.equal(byId.ibra.locked, true);
    assert.equal(byId.modou.locked, false);
    assert.equal(byId.modou.status, 'Present', 'statut par défaut d\'une grille vierge');
});

test('un retard historique sans billet est affiché en lecture seule', async () => {
    const { view } = await boot({
        roster: () => ({ alreadySubmitted: true, students: [{ studentId: 'awa', matricule: 'E1', fullName: 'Awa Fall', status: 'Late', lateMinutes: 5 }] })
    });

    assert.equal(view.entries[0].locked, true);
    assert.equal(view.rowStatusLabel(view.entries[0]), 'Retard (5 min)');
});

test('le libellé d\'une ligne verrouillée dit d\'où elle vient', async () => {
    const { view } = await boot();

    const byId = Object.fromEntries(view.entries.map((e) => [e.studentId, e]));
    assert.equal(view.rowStatusLabel(byId.awa), 'Retard (12 min) — billet');
    assert.equal(view.rowStatusLabel(byId.ibra), 'Absent (justifié) — billet');
});

test('la soumission n\'envoie des minutes que pour une ligne en retard issue d\'un billet', async () => {
    const { view, calls } = await boot();
    view.entries.find((e) => e.studentId === 'modou').status = 'UnjustifiedAbsence';

    await view.submit();

    const sent = Object.fromEntries(calls.post[0].body.entries.map((e) => [e.studentId, e]));
    assert.deepEqual(sent.awa, { studentId: 'awa', status: 'Late', lateMinutes: 12 }, 'renvoyée telle quelle');
    assert.deepEqual(sent.ibra, { studentId: 'ibra', status: 'JustifiedAbsence', lateMinutes: 0 });
    assert.deepEqual(sent.modou, { studentId: 'modou', status: 'UnjustifiedAbsence', lateMinutes: 0 });
});

test('un élève en retard (billet) compte parmi les présents', async () => {
    const { view } = await boot();

    // Awa (retard, billet) + Modou (présent par défaut) ; Ibra est absent justifié.
    assert.equal(view.presentCount, 2);
    assert.equal(view.countBy('JustifiedAbsence'), 1);
});
