/**
 * Écran d'appel par créneau et billets d'entrée (Évolution N°5) — côté navigateur.
 *
 *   1. Mode « Par créneau » par défaut quand la classe a des cours ce jour-là, « Libre » sinon.
 *   2. Choisir un cours charge la feuille avec `scheduleSlotId` et SANS période saisie.
 *   3. La soumission en mode créneau envoie `scheduleSlotId`, jamais une période.
 *   4. Un jour de repos n'envoie aucune requête de cours ni de feuille.
 *   5. Un billet d'entrée s'affiche sur la ligne de l'élève ; « Accepter » n'est proposé qu'à l'enseignant du
 *      cours (ou au Directeur), et seulement tant que le billet est en attente.
 *
 * Confort d'affichage : les règles vivent côté serveur (créneau, titulaire, jour de repos).
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
    { slotId: 's1', subjectId: 'maths', subjectName: 'Mathématiques', teacherName: 'Awa Sow', start: '08:00:00', end: '10:00:00', label: '08:00-10:00', sheetId: null },
    { slotId: 's2', subjectId: 'fr', subjectName: 'Français', teacherName: 'Modou Ba', start: '10:00:00', end: '12:00:00', label: '10:00-12:00', sheetId: 'sheet-2' }
];

const ROSTER = () => ({
    alreadySubmitted: false,
    students: [
        { studentId: 'awa', matricule: 'E1', fullName: 'Awa Fall', status: 'Late', lateMinutes: 12,
          entryTicketId: 'tk1', entryTicketNumber: 'BILLET-AAAA1111', entryTicketStatus: 'Issued' },
        { studentId: 'modou', matricule: 'E2', fullName: 'Modou Diop', status: null, lateMinutes: 0 }
    ]
});

/** Charge auth.js (session posée) + attendance.js ; `slots` = réponse de GET /attendance/slots. */
async function boot({ role = 'Directeur', slots = SLOTS, workingDays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'], roster = ROSTER } = {}) {
    const calls = { get: [], post: [] };
    const ctx = loadScripts(['auth.js', 'attendance.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    calls.get.push(endpoint);
                    if (endpoint === '/schools/current/settings') return { workingDays };
                    if (endpoint.startsWith('/attendance/slots')) return slots;
                    if (endpoint.startsWith('/attendance/roster')) return roster();
                    if (endpoint === '/classrooms') return [{ id: 'c1', name: 'CM2 A', level: 'Primaire' }];
                    if (endpoint === '/subjects') return [{ id: 'maths', name: 'Mathématiques', level: 'Primaire' }];
                    return [];
                },
                post: async (endpoint, body) => { calls.post.push({ endpoint, body: plain(body) }); return {}; },
                postWithRetry: async (endpoint, body) => { calls.post.push({ endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback
            },
            location: { href: 'https://localhost/presences', search: '' }
        }
    });
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role }), expiresIn: 900 });
    await ctx.store('schoolConfig').init();
    const view = ctx.component('attendanceView');
    await flush();
    return { view, calls };
}

/** Un samedi (jour ouvré par défaut) : le test ne dépend pas du jour où il tourne. */
async function pickClassroom(view, date = '2026-09-26') {
    view.filters.date = date;
    view.filters.classroomId = 'c1';
    await view.loadSlots();
}

// ---------------------------------------------------------------- 1. Mode

test('le mode « par créneau » est celui par défaut quand la classe a des cours ce jour-là', async () => {
    const { view } = await boot();

    await pickClassroom(view);

    assert.equal(view.slots.length, 2);
    assert.equal(view.slotMode, true);
});

test('sans cours ce jour-là, l\'appel reste libre (demi-journée) comme avant', async () => {
    const { view } = await boot({ slots: [] });

    await pickClassroom(view);

    assert.equal(view.slotMode, false);
    view.filters.subjectId = 'maths';
    view.filters.period = 'Matin';
    assert.equal(Boolean(view.canLoad), true, 'le mode libre exige matière et période');
});

test('on peut basculer en appel libre même quand des cours existent, et revenir', async () => {
    const { view } = await boot();
    await pickClassroom(view);

    view.setMode('free');
    assert.equal(view.slotMode, false);

    view.setMode('slot');
    assert.equal(view.slotMode, true);
});

// ---------------------------------------------------------------- 2. Chargement de la feuille

test('choisir un cours charge la feuille avec scheduleSlotId et sans période', async () => {
    const { view, calls } = await boot();
    await pickClassroom(view);

    await view.selectSlot(view.slots[0]);

    const request = calls.get.find((u) => u.startsWith('/attendance/roster'));
    assert.ok(request, 'la feuille est demandée');
    assert.ok(request.includes('scheduleSlotId=s1'));
    assert.ok(request.includes('subjectId=maths'), 'la matière est celle du cours');
    assert.equal(request.includes('period='), false, 'aucune période saisie n\'est envoyée');
    assert.equal(view.rosterLoaded, true);
});

test('en mode créneau, canLoad exige un cours choisi', async () => {
    const { view } = await boot();
    await pickClassroom(view);

    assert.equal(Boolean(view.canLoad), false, 'aucun cours choisi');
    view.selectedSlotId = 's1';
    assert.equal(Boolean(view.canLoad), true);
});

// ---------------------------------------------------------------- 3. Soumission

test('la soumission par créneau envoie scheduleSlotId et jamais de période', async () => {
    const { view, calls } = await boot();
    await pickClassroom(view);
    await view.selectSlot(view.slots[0]);

    await view.submit();

    const sent = calls.post.find((p) => p.endpoint === '/attendance');
    assert.equal(sent.body.scheduleSlotId, 's1');
    assert.equal(sent.body.subjectId, 'maths');
    assert.equal('period' in sent.body, false);
});

test('la soumission libre envoie la période saisie et pas de créneau', async () => {
    const { view, calls } = await boot({ slots: [] });
    await pickClassroom(view);
    Object.assign(view.filters, { subjectId: 'maths', period: 'Matin' });
    await view.loadRoster();

    await view.submit();

    const sent = calls.post.find((p) => p.endpoint === '/attendance');
    assert.equal(sent.body.period, 'Matin');
    assert.equal('scheduleSlotId' in sent.body, false);
});

// ---------------------------------------------------------------- 4. Jour de repos

test('un jour de repos ne demande ni cours ni feuille', async () => {
    const { view, calls } = await boot({ workingDays: ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday'] });

    await pickClassroom(view, '2026-09-24'); // jeudi : repos

    assert.equal(view.isRestDay, true);
    assert.equal(calls.get.some((u) => u.startsWith('/attendance/slots')), false);
    assert.equal(view.slots.length, 0);
});

// ---------------------------------------------------------------- 5. Billets d'entrée

test('un billet d\'entrée s\'affiche sur la ligne de l\'élève avec son statut', async () => {
    const { view } = await boot();
    await pickClassroom(view);
    await view.selectSlot(view.slots[0]);

    const awa = view.entries.find((e) => e.studentId === 'awa');
    assert.equal(awa.status, 'Late', 'le retard est présélectionné par le serveur');
    assert.equal(awa.entryTicketId, 'tk1');
    assert.equal(view.ticketStatusLabel(awa.entryTicketStatus), 'en attente d\'acceptation');
    assert.equal(view.entries.find((e) => e.studentId === 'modou').entryTicketId, null);
});

test('« Accepter » est proposé à l\'Enseignant et au Directeur, tant que le billet est en attente', async () => {
    for (const role of ['Enseignant', 'Directeur']) {
        const { view } = await boot({ role });
        await pickClassroom(view);
        await view.selectSlot(view.slots[0]);

        const awa = view.entries.find((e) => e.studentId === 'awa');
        assert.equal(view.canAcceptTicket(awa), true, role);
        assert.equal(view.canAcceptTicket(view.entries.find((e) => e.studentId === 'modou')), false, 'pas de billet');
    }
});

test('« Accepter » n\'est pas proposé à la Vie Scolaire ni au Secrétariat', async () => {
    for (const role of ['Surveillant', 'Secretariat']) {
        const { view } = await boot({ role });
        await pickClassroom(view);
        await view.selectSlot(view.slots[0]);

        assert.equal(view.canAcceptTicket(view.entries.find((e) => e.studentId === 'awa')), false, role);
    }
});

test('un billet déjà accepté ne se ré-accepte pas', async () => {
    const { view } = await boot({
        roster: () => ({ ...ROSTER(), students: [{ studentId: 'awa', matricule: 'E1', fullName: 'Awa', status: 'Late', lateMinutes: 5,
            entryTicketId: 'tk1', entryTicketNumber: 'B', entryTicketStatus: 'Accepted' }] })
    });
    await pickClassroom(view);
    await view.selectSlot(view.slots[0]);

    assert.equal(view.canAcceptTicket(view.entries[0]), false);
});

test('accepter appelle POST /billets/{id}/accept puis recharge la feuille', async () => {
    const { view, calls } = await boot({ role: 'Enseignant' });
    await pickClassroom(view);
    await view.selectSlot(view.slots[0]);
    const rostersBefore = calls.get.filter((u) => u.startsWith('/attendance/roster')).length;

    await view.acceptTicket(view.entries.find((e) => e.studentId === 'awa'));

    assert.ok(calls.post.some((p) => p.endpoint === '/billets/tk1/accept'));
    assert.equal(calls.get.filter((u) => u.startsWith('/attendance/roster')).length, rostersBefore + 1, 'la feuille est rechargée');
    assert.match(view.ticketNotice, /accepté/i);
});
