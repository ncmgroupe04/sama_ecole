/**
 * Écran des billets d'entrée (Évolution N°5) — côté navigateur.
 *
 *   1. Le cours visé est proposé d'après la classe de l'élève et la date ; le cours en cours (à défaut le
 *      prochain) est présélectionné.
 *   2. Le retard est créé avec `targetScheduleSlotId` (ou null : billet sans cours précis, comme avant).
 *   3. Le statut du billet se lit dans la liste ; « Annuler » n'est proposé qu'à la Vie Scolaire / au
 *      Directeur, et seulement tant que le billet est en attente.
 *   4. Annuler appelle POST /billets/{id}/cancel puis recharge la liste ; un refus serveur s'affiche.
 *
 * Confort d'affichage : les règles vivent côté serveur (cours de la classe, rôle, statut).
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
    { slotId: 's1', subjectId: 'maths', subjectName: 'Mathématiques', teacherName: 'Awa Sow', start: '08:00:00', end: '10:00:00', label: '08:00-10:00', isCurrent: false, isNext: false, ticketStatus: null },
    { slotId: 's2', subjectId: 'fr', subjectName: 'Français', teacherName: 'Modou Ba', start: '10:00:00', end: '12:00:00', label: '10:00-12:00', isCurrent: true, isNext: false, ticketStatus: null },
    { slotId: 's3', subjectId: 'svt', subjectName: 'SVT', teacherName: 'Fatou Ndiaye', start: '14:00:00', end: '16:00:00', label: '14:00-16:00', isCurrent: false, isNext: true, ticketStatus: null }
];

const late = (id, status) => ({
    id, studentId: 'awa', studentFullName: 'Awa Fall', studentMatricule: 'E1', date: '2026-09-26T00:00:00',
    minutes: 10, reason: 'Transport', targetScheduleSlotId: status ? 's1' : null, status
});

async function boot({ role = 'Surveillant', slots = SLOTS, lateArrivals = [late('l1', 'Issued')], failPost = null } = {}) {
    const calls = { get: [], post: [], toasts: [] };
    const api = {
        get: async (endpoint) => {
            calls.get.push(endpoint);
            if (endpoint.startsWith('/absences/today-slots')) return slots;
            if (endpoint === '/absences/late-arrivals') return lateArrivals;
            return [];
        },
        getAllPages: async () => [{ id: 'awa', fullName: 'Awa Fall', matricule: 'E1' }],
        post: async (endpoint, body) => {
            if (failPost) throw failPost;
            calls.post.push({ endpoint, body: plain(body) });
            return {};
        },
        toMessage: (e, fallback) => (e && e.message) || fallback,
        toFieldErrors: (_e, fallback) => ({ global: fallback })
    };
    const toast = { error: (m) => calls.toasts.push(['error', m]), success: (m) => calls.toasts.push(['success', m]) };

    const ctx = loadScripts(['auth.js', 'pdf-preview.js', 'billets.js'], { preload: { api } });
    ctx.sandbox.api = api;
    ctx.sandbox.toast = toast;
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role }), expiresIn: 900 });
    const view = ctx.component('billetsView');
    await flush();
    return { view, calls };
}

// ---------------------------------------------------------------- 1. Cours visé

test('les cours du jour de la classe de l\'élève sont demandés avec l\'élève et la date', async () => {
    const { view, calls } = await boot();

    view.form.studentId = 'awa';
    view.form.date = '2026-09-26';
    await view.loadTodaySlots();

    const request = calls.get.find((u) => u.startsWith('/absences/today-slots'));
    assert.ok(request.includes('studentId=awa'));
    assert.ok(request.includes('date=2026-09-26'));
    assert.equal(view.todaySlots.length, 3);
});

test('le cours en cours est présélectionné', async () => {
    const { view } = await boot();
    view.form.studentId = 'awa';

    await view.loadTodaySlots();

    assert.equal(view.form.targetScheduleSlotId, 's2');
});

test('à défaut de cours en cours, le prochain est présélectionné', async () => {
    const { view } = await boot({ slots: SLOTS.map((s) => ({ ...s, isCurrent: false })) });
    view.form.studentId = 'awa';

    await view.loadTodaySlots();

    assert.equal(view.form.targetScheduleSlotId, 's3');
});

test('sans cours ce jour-là, rien n\'est présélectionné (billet sans cours précis)', async () => {
    const { view } = await boot({ slots: [] });
    view.form.studentId = 'awa';

    await view.loadTodaySlots();

    assert.equal(view.todaySlots.length, 0);
    assert.equal(view.form.targetScheduleSlotId, '');
});

test('sans élève choisi, aucune requête de cours n\'est envoyée', async () => {
    const { view, calls } = await boot();

    await view.loadTodaySlots();

    assert.equal(calls.get.some((u) => u.startsWith('/absences/today-slots')), false);
});

test('le libellé d\'un cours indique l\'horaire, la matière, l\'enseignant et son état', async () => {
    const { view } = await boot();

    assert.equal(view.slotOptionLabel(SLOTS[1]), '10:00-12:00 · Français (Modou Ba) — en cours');
    assert.equal(view.slotOptionLabel({ ...SLOTS[0], ticketStatus: 'Issued' }), '08:00-10:00 · Mathématiques (Awa Sow) — billet déjà émis');
});

// ---------------------------------------------------------------- 2. Création

test('le retard est créé avec le cours visé', async () => {
    const { view, calls } = await boot();
    Object.assign(view.form, { studentId: 'awa', minutes: 10, reason: 'Transport', targetScheduleSlotId: 's2' });

    await view.submitCreate();

    const sent = calls.post.find((p) => p.endpoint === '/absences/late-arrivals');
    assert.equal(sent.body.targetScheduleSlotId, 's2');
    assert.equal(sent.body.minutes, 10);
});

test('« Sans cours précis » envoie null, comme avant', async () => {
    const { view, calls } = await boot();
    Object.assign(view.form, { studentId: 'awa', minutes: 10, reason: 'Transport', targetScheduleSlotId: '' });

    await view.submitCreate();

    assert.equal(calls.post.find((p) => p.endpoint === '/absences/late-arrivals').body.targetScheduleSlotId, null);
});

// ---------------------------------------------------------------- 3. Statut et annulation

test('les libellés de statut sont ceux du vocabulaire du billet', async () => {
    const { view } = await boot();

    assert.equal(view.ticketStatusLabel('Issued'), 'En attente d\'acceptation');
    assert.equal(view.ticketStatusLabel('Accepted'), 'Accepté en classe');
    assert.equal(view.ticketStatusLabel('Cancelled'), 'Annulé');
    assert.equal(view.ticketStatusLabel(null), '', 'un retard sans cours visé n\'a pas de statut');
});

test('« Annuler » est proposé à la Vie Scolaire et au Directeur, pour un billet en attente seulement', async () => {
    for (const role of ['Surveillant', 'Directeur']) {
        const { view } = await boot({ role });

        assert.equal(view.canCancelTicket(late('a', 'Issued')), true, role);
        assert.equal(view.canCancelTicket(late('b', 'Accepted')), false, `${role} : déjà accepté`);
        assert.equal(view.canCancelTicket(late('c', 'Cancelled')), false, `${role} : déjà annulé`);
        assert.equal(view.canCancelTicket(late('d', null)), false, `${role} : retard sans cours visé`);
    }
});

test('« Annuler » n\'est pas proposé au Secrétariat ni à l\'Enseignant', async () => {
    for (const role of ['Secretariat', 'Enseignant']) {
        const { view } = await boot({ role });

        assert.equal(view.canCancelTicket(late('a', 'Issued')), false, role);
    }
});

// ---------------------------------------------------------------- 4. Annulation

test('l\'annulation exige une confirmation puis appelle POST /billets/{id}/cancel et recharge la liste', async () => {
    const { view, calls } = await boot();
    const item = late('l1', 'Issued');

    view.askCancelTicket(item);
    assert.equal(view.ticketToCancel.id, 'l1');
    assert.equal(calls.post.length, 0, 'rien n\'est envoyé avant confirmation');

    const loadsBefore = calls.get.filter((u) => u === '/absences/late-arrivals').length;
    await view.confirmCancelTicket();

    assert.ok(calls.post.some((p) => p.endpoint === '/billets/l1/cancel'));
    assert.equal(view.ticketToCancel, null, 'la confirmation se ferme');
    assert.equal(calls.get.filter((u) => u === '/absences/late-arrivals').length, loadsBefore + 1, 'la liste est rechargée');
    assert.deepEqual(calls.toasts.at(-1), ['success', 'Billet annulé.']);
});

test('un refus du serveur (billet déjà accepté) s\'affiche dans la confirmation, qui reste ouverte', async () => {
    const { view } = await boot({ failPost: new Error('Ce billet a déjà été accepté par l\'enseignant.') });

    view.askCancelTicket(late('l1', 'Issued'));
    await view.confirmCancelTicket();

    assert.equal(view.cancelError, 'Ce billet a déjà été accepté par l\'enseignant.');
    assert.notEqual(view.ticketToCancel, null);
});

test('fermer la confirmation abandonne l\'annulation', async () => {
    const { view, calls } = await boot();

    view.askCancelTicket(late('l1', 'Issued'));
    view.closeCancel();

    assert.equal(view.ticketToCancel, null);
    assert.equal(calls.post.length, 0);
});
