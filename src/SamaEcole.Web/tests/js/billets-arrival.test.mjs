/**
 * Écran des billets d'entrée par HEURE D'ARRIVÉE (Complément N°5 bis, Tâche 14) — côté navigateur.
 *
 *   1. Avec des cours ce jour-là, l'heure d'arrivée remplace les minutes ; sans cours, l'ancien champ reste.
 *   2. L'aperçu vient du serveur (GET /absences/arrival-preview) : aucun calcul de durée côté client.
 *   3. « Enregistrer » exige un aperçu valide ; un refus serveur (avant le premier cours…) bloque et s'affiche.
 *   4. Une réponse d'aperçu périmée (l'utilisateur a changé l'heure entre-temps) est ignorée.
 *   5. Le billet part avec l'heure d'arrivée SEULE (ni minutes, ni cours visé) ; sans cours, avec les minutes.
 *   6. La liste indique l'arrivée et la durée régularisée d'un billet par heure d'arrivée.
 *
 * Confort d'affichage : les règles vivent côté serveur (ArrivalCoverage / ArrivalPlanner).
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
    { slotId: 's2', subjectId: 'fr', subjectName: 'Français', teacherName: 'Modou Ba', start: '10:00:00', end: '12:00:00', label: '10:00-12:00', isCurrent: true, isNext: false, ticketStatus: null }
];

const PREVIEW = {
    missedSlots: [{ slotId: 's1', label: '08:00-10:00', subjectName: 'Mathématiques', minutes: 120 }],
    inProgress: { slotId: 's2', label: '10:00-12:00', subjectName: 'Français', lateMinutes: 20 },
    targetSlotId: 's2', missedMinutes: 120, lateMinutes: 20, totalMinutes: 140
};

/** `preview` : (arrivalTime) => Promise<réponse> ou lève ; permet de piloter l'ordre des réponses. */
async function boot({ slots = SLOTS, preview = async () => PREVIEW } = {}) {
    const calls = { get: [], post: [] };
    const api = {
        get: async (endpoint) => {
            calls.get.push(endpoint);
            if (endpoint.startsWith('/absences/today-slots')) return slots;
            if (endpoint.startsWith('/absences/arrival-preview')) {
                return preview(new URLSearchParams(endpoint.split('?')[1]).get('arrivalTime'));
            }
            if (endpoint === '/absences/late-arrivals') return [];
            return [];
        },
        getAllPages: async () => [{ id: 'awa', fullName: 'Awa Fall', matricule: 'E1' }],
        post: async (endpoint, body) => { calls.post.push({ endpoint, body: plain(body) }); return {}; },
        toMessage: (e, fallback) => (e && e.message) || fallback,
        toFieldErrors: (e, fallback) => (e && e.fields ? e.fields : { global: fallback })
    };
    const toast = { error() {}, success() {} };

    const ctx = loadScripts(['auth.js', 'pdf-preview.js', 'billets.js'], { preload: { api } });
    ctx.sandbox.api = api;
    ctx.sandbox.toast = toast;
    ctx.sandbox.setTimeout = (fn) => { fn(); return 0; }; // pas de debounce réel en test
    ctx.sandbox.clearTimeout = () => {};
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role: 'Surveillant' }), expiresIn: 900 });
    const view = ctx.component('billetsView');
    await flush();

    view.students = [{ id: 'awa', fullName: 'Awa Fall', matricule: 'E1' }];
    Object.assign(view.form, { studentId: 'awa', date: '2026-09-26', reason: 'Transport' });
    await view.loadTodaySlots();
    return { view, calls };
}

// ---------------------------------------------------------------- 1. Quel champ ?

test('avec des cours ce jour-là, l\'heure d\'arrivée remplace les minutes', async () => {
    const { view } = await boot();

    assert.equal(view.hasSlots, true);
});

test('sans cours ce jour-là, l\'ancien champ « minutes » reste et l\'enregistrement n\'attend aucun aperçu', async () => {
    const { view } = await boot({ slots: [] });

    assert.equal(view.hasSlots, false);
    assert.equal(view.canSubmitCreate, true);
});

// ---------------------------------------------------------------- 2. Aperçu serveur

test('l\'aperçu est demandé au serveur avec l\'élève, la date et l\'heure au format HH:mm:ss', async () => {
    const { view, calls } = await boot();

    view.form.arrivalTime = '10:20';
    await view.loadArrivalPreview();

    const request = calls.get.find((u) => u.startsWith('/absences/arrival-preview'));
    assert.ok(request.includes('studentId=awa'));
    assert.ok(request.includes('date=2026-09-26'));
    assert.ok(request.includes('arrivalTime=10%3A20%3A00'), request);
    assert.equal(view.arrivalPreview.totalMinutes, 140, 'la durée vient du serveur, jamais recalculée ici');
});

test('aucune requête d\'aperçu sans heure d\'arrivée', async () => {
    const { view, calls } = await boot();

    await view.loadArrivalPreview();

    assert.equal(calls.get.some((u) => u.startsWith('/absences/arrival-preview')), false);
});

test('la durée se lit en clair', async () => {
    const { view } = await boot();

    assert.equal(view.formatDuration(15), '15 min');
    assert.equal(view.formatDuration(120), '2 h');
    assert.equal(view.formatDuration(140), '2 h 20');
    assert.equal(view.formatDuration(185), '3 h 05');
});

// ---------------------------------------------------------------- 3. Enregistrement

test('« Enregistrer » attend un aperçu valide quand la classe a des cours', async () => {
    const { view } = await boot();
    assert.equal(view.canSubmitCreate, false, 'pas d\'heure, pas d\'aperçu');

    view.form.arrivalTime = '10:20';
    await view.loadArrivalPreview();

    assert.equal(view.canSubmitCreate, true);
});

test('un refus serveur (arrivée avant le premier cours) s\'affiche et bloque l\'enregistrement', async () => {
    const refusal = { fields: { arrivaltime: 'Aucun cours n\'a commencé à cette heure-là : le premier cours débute à 08:00.' } };
    const { view } = await boot({ preview: async () => { throw refusal; } });

    view.form.arrivalTime = '07:30';
    await view.loadArrivalPreview();

    assert.match(view.arrivalError, /premier cours débute à 08:00/);
    assert.equal(view.arrivalPreview, null);
    assert.equal(view.canSubmitCreate, false);
});

// ---------------------------------------------------------------- 4. Réponse périmée

test('la réponse d\'un aperçu périmé est ignorée', async () => {
    const resolvers = {};
    const { view } = await boot({
        preview: (arrival) => new Promise((resolve) => { resolvers[arrival] = resolve; })
    });

    view.form.arrivalTime = '10:20';
    const first = view.loadArrivalPreview();
    view.form.arrivalTime = '10:40';
    const second = view.loadArrivalPreview();

    resolvers['10:40:00']({ ...PREVIEW, totalMinutes: 160 });
    await second;
    resolvers['10:20:00']({ ...PREVIEW, totalMinutes: 140 }); // arrive APRÈS, mais périmée
    await first;

    assert.equal(view.arrivalPreview.totalMinutes, 160);
});

// ---------------------------------------------------------------- 5. Émission

test('avec des cours, le billet part avec l\'heure d\'arrivée seule — ni minutes, ni cours visé', async () => {
    const { view, calls } = await boot();
    Object.assign(view.form, { arrivalTime: '10:20', minutes: 999 });
    await view.loadArrivalPreview();

    await view.submitCreate();

    const sent = calls.post.find((p) => p.endpoint === '/absences/late-arrivals').body;
    assert.equal(sent.arrivalTime, '10:20:00');
    assert.equal('minutes' in sent, false);
    assert.equal('targetScheduleSlotId' in sent, false);
    assert.equal(sent.studentId, 'awa');
});

test('sans cours, le billet part avec les minutes saisies, comme avant', async () => {
    const { view, calls } = await boot({ slots: [] });
    view.form.minutes = 12;

    await view.submitCreate();

    const sent = calls.post.find((p) => p.endpoint === '/absences/late-arrivals').body;
    assert.equal(sent.minutes, 12);
    assert.equal('arrivalTime' in sent, false);
});

test('changer d\'élève ou de date efface l\'heure et l\'aperçu', async () => {
    const { view } = await boot();
    view.form.arrivalTime = '10:20';
    await view.loadArrivalPreview();

    await view.loadTodaySlots();

    assert.equal(view.form.arrivalTime, '');
    assert.equal(view.arrivalPreview, null);
});

// ---------------------------------------------------------------- 6. Liste

test('la liste indique l\'arrivée et la durée régularisée, ou les minutes d\'un billet à l\'ancienne', async () => {
    const { view } = await boot();

    assert.equal(view.lateCellText({ minutes: 15, arrivalTime: null, totalMinutes: null }), '15 min');
    assert.equal(view.lateCellText({ minutes: 20, arrivalTime: '10:20:00', totalMinutes: 140 }), 'Arrivé à 10:20 · 2 h 20');
});
