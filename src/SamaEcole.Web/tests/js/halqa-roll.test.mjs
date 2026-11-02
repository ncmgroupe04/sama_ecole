/**
 * Appel de la Halqa de l'Oustaz (wwwroot/js/halqa-roll.js) — composant `halqaRoll`, en arabe, de droite à gauche.
 *
 * Enjeux : (1) le bouton n'existe que s'il y a une session de Halqa active ; (2) la feuille est celle de LA Halqa de l'Oustaz (aucun identifiant de
 * groupe n'est envoyé : le serveur l'impose) ; (3) seules les lignes décidées et modifiées partent, avec des `studentId` ; (4) un résultat mixte garde
 * chaque refus visible, en arabe ; (5) une date future est refusée avant tout appel ; (6) une saisie non enregistrée n'est pas jetée en silence.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const SESSION = { id: 's-halqa', name: 'Appel de la Halqa', audience: 'Halqa', days: [1, 2, 3, 4, 5, 6, 7], timeLabel: null, isActive: true, sortOrder: 0, hasRecords: false };
const EVENING = { ...SESSION, id: 's-evening', name: 'Halqa du soir', timeLabel: '20 h' };

const LINE = (id, name, extra = {}) => ({ studentId: id, studentName: name, studentNameAr: null, matricule: `M-${id}`, detail: null, onLeave: false, recorded: false, isPresent: null, note: null, ...extra });

const sheetOf = (lines, extra = {}) => ({ sessionId: 's-halqa', sessionName: 'Appel de la Halqa', audience: 'Halqa', groupId: 'o1', groupName: 'Oustaz Un', date: '2026-10-07', outsideSessionDays: false, lines, ...extra });

function apiError(code, status = 409, message = 'technique') {
    const error = new Error(message);
    error.code = code;
    error.status = status;
    return error;
}

function boot({ sessions = [SESSION], lines = [LINE('a', 'Awa'), LINE('b', 'Binta', { studentNameAr: 'بنتا' }), LINE('c', 'Cheikh', { recorded: true, isPresent: true })],
    fail = () => undefined, post, sheet, confirm = () => true } = {}) {
    const calls = [];
    const record = async (method, endpoint, body, result) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        const error = fail(method, endpoint);
        if (error) throw error;
        return result;
    };
    const route = (endpoint) => {
        if (endpoint === '/internat/attendance/sessions') return sessions;
        if (endpoint.startsWith('/internat/attendance/sheet')) return sheet ? sheet(endpoint) : sheetOf(lines);
        return null;
    };
    const defaultPost = (body) => ({
        sessionId: body.sessionId, groupId: 'o1', date: body.date, saved: body.entries.length, rejected: 0,
        lines: body.entries.map((e) => ({ studentId: e.studentId, outcome: 'Saved', code: null, message: null }))
    });
    const ctx = loadScripts(['attendance-logic.js', 'halqa-roll.js'], {
        preload: {
            auth: { role: 'Enseignant' },
            confirm,
            api: {
                get: (endpoint) => record('GET', endpoint, undefined, route(endpoint)),
                post: (endpoint, body) => record('POST', endpoint, body, (post || defaultPost)(plain(body))),
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : (err && err.message) || fallback)
            }
        }
    });
    const roll = () => {
        const r = ctx.component('halqaRoll');
        r.today = () => '2026-10-07';
        r.date = '2026-10-07';
        return r;
    };
    return { ctx, calls, roll };
}

async function openRoll(options) {
    const env = boot(options);
    const r = env.roll();
    await flush();
    await r.openRoll();
    return { ...env, r };
}

const byId = (r, id) => r.lines.find((l) => l.boarderId === id);

test('sans session de Halqa active : pas de bouton, aucun appel d\'appel inutile', async () => {
    const env = boot({ sessions: [] });
    const r = env.roll();
    await flush();

    assert.equal(r.canRoll, false);
    await r.openRoll();
    assert.equal(r.isOpen, false);
    assert.equal(env.calls.some((c) => c.endpoint.startsWith('/internat/attendance/sheet')), false);
});

test('une session active : le bouton existe, la session est choisie', async () => {
    const env = boot();
    const r = env.roll();
    await flush();

    assert.equal(r.canRoll, true);
    assert.equal(r.sessionId, 's-halqa');
    assert.ok(env.calls.some((c) => c.endpoint === '/internat/attendance/sessions'));
});

test('échec du chargement des sessions : le bouton reste simplement absent, sans erreur bloquante', async () => {
    const env = boot({ fail: (m, e) => (e === '/internat/attendance/sessions' ? apiError('MODULE_DISABLED', 403) : undefined) });
    const r = env.roll();
    await flush();

    assert.equal(r.canRoll, false);
    assert.equal(r.error, '');
});

test('ouvrir l\'appel charge la feuille de LA Halqa : session et jour seulement, jamais de groupe', async () => {
    const { r, calls } = await openRoll();

    const sheet = calls.find((c) => c.endpoint.startsWith('/internat/attendance/sheet'));
    assert.equal(sheet.endpoint, '/internat/attendance/sheet?sessionId=s-halqa&date=2026-10-07');
    assert.doesNotMatch(sheet.endpoint, /instructorId|groupId/);
    assert.equal(r.isOpen, true);
    assert.equal(r.lines.length, 3);
});

test('le nom arabe est préféré, le nom français sert de repli', async () => {
    const { r } = await openRoll();

    assert.equal(r.displayName(byId(r, 'b')), 'بنتا');
    assert.equal(r.displayName(byId(r, 'a')), 'Awa');
});

test('seules les lignes décidées et modifiées partent, avec des studentId', async () => {
    const { r, calls } = await openRoll();
    r.setPresence(byId(r, 'a'), true);
    r.setPresence(byId(r, 'b'), false);
    byId(r, 'b').note = '  Malade ';
    await r.save();

    const post = calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/internat/attendance');
    assert.deepEqual(post.body, {
        sessionId: 's-halqa', date: '2026-10-07',
        entries: [{ studentId: 'a', isPresent: true, note: null }, { studentId: 'b', isPresent: false, note: 'Malade' }]
    });
    assert.match(r.notice, /تم حفظ 2/);
    assert.equal(r.counts.dirty, 0);
});

test('rien à enregistrer : aucun appel, message en arabe', async () => {
    const { r, calls } = await openRoll({ lines: [LINE('c', 'Cheikh', { recorded: true, isPresent: true })] });
    await r.save();

    assert.equal(calls.some((c) => c.method === 'POST'), false);
    assert.equal(r.notice, r.labels.noChange);
});

test('« الكل حاضر » coche les lignes non décidées seulement', async () => {
    const { r } = await openRoll({ lines: [LINE('a', 'Awa'), LINE('b', 'Binta', { onLeave: true, isPresent: false, note: 'En permission' }), LINE('c', 'Cheikh', { isPresent: false, recorded: true })] });
    r.markAllPresent();

    assert.equal(byId(r, 'a').isPresent, true);
    assert.equal(byId(r, 'b').isPresent, false, 'en permission : reste absent');
    assert.equal(byId(r, 'c').isPresent, false, 'décision déjà prise');
});

test('résultat mixte : le refus reste sur sa ligne, en arabe, sans masquer les succès', async () => {
    const post = (body) => ({
        sessionId: body.sessionId, groupId: 'o1', date: body.date, saved: 1, rejected: 1,
        lines: [
            { studentId: 'a', outcome: 'Saved', code: null, message: null },
            { studentId: 'b', outcome: 'Rejected', code: 'NOT_IN_GROUP', message: 'x' }
        ]
    });
    const { r } = await openRoll({ post });
    r.setPresence(byId(r, 'a'), true);
    r.setPresence(byId(r, 'b'), true);
    await r.save();

    assert.equal(byId(r, 'a').recorded, true);
    assert.equal(byId(r, 'b').rejection, r.labels.rejections.NOT_IN_GROUP);
    assert.equal(r.rejectedLines.length, 1);
    assert.equal(r.notice, r.labels.savedWithRejected(1, 1));
    assert.equal(r.counts.dirty, 1, 'la ligne refusée reste à traiter');
});

test('session désactivée entre-temps : message en arabe, saisie conservée', async () => {
    const { r } = await openRoll({ fail: (m) => (m === 'POST' ? apiError('ATTENDANCE_SESSION_INACTIVE') : undefined) });
    r.setPresence(byId(r, 'a'), true);
    await r.save();

    assert.equal(r.error, r.labels.inactive);
    assert.equal(byId(r, 'a').isPresent, true);
    assert.equal(r.saving, false);
});

test('conflit d\'écriture : message en arabe et feuille relue', async () => {
    let fail = true;
    const { r, calls } = await openRoll({ fail: (m) => (m === 'POST' && fail ? apiError('ATTENDANCE_CONFLICT') : undefined) });
    const before = calls.filter((c) => c.endpoint.startsWith('/internat/attendance/sheet')).length;
    r.setPresence(byId(r, 'a'), true);
    await r.save();
    fail = false;

    assert.equal(r.error, r.labels.conflict);
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/internat/attendance/sheet')).length, before + 1);
});

test('date future : refusée avant tout appel, message en arabe', async () => {
    const { r, calls } = await openRoll();
    const before = calls.length;
    r.date = '2026-10-08';
    await r.onSelectionChange();

    assert.equal(calls.length, before);
    assert.equal(r.dateError, r.labels.futureDate);

    r.date = '';
    await r.onSelectionChange();
    assert.equal(r.dateError, r.labels.emptyDate);
});

test('changer de jour avec une saisie non enregistrée : confirmation ; refusée, rien ne change', async () => {
    const { r, calls } = await openRoll({ confirm: () => false });
    r.setPresence(byId(r, 'a'), true);
    const before = calls.length;
    r.date = '2026-10-06';
    await r.onSelectionChange();

    assert.equal(calls.length, before, 'aucun rechargement');
    assert.equal(r.date, '2026-10-07', 'le jour de la feuille affichée est restauré');
    assert.equal(byId(r, 'a').isPresent, true);
});

test('changer de jour avec confirmation : la nouvelle feuille est chargée', async () => {
    const { r, calls } = await openRoll({ confirm: () => true });
    r.setPresence(byId(r, 'a'), true);
    r.date = '2026-10-06';
    await r.onSelectionChange();

    assert.ok(calls.some((c) => c.endpoint.endsWith('date=2026-10-06')));
});

test('sans saisie en attente, changer de session charge directement la nouvelle feuille', async () => {
    let asked = false;
    const { r, calls } = await openRoll({ sessions: [SESSION, EVENING], confirm: () => { asked = true; return true; } });
    r.sessionId = 's-evening';
    await r.onSelectionChange();

    assert.equal(asked, false);
    assert.ok(calls.some((c) => c.endpoint.includes('sessionId=s-evening')));
});

test('un jour hors des jours de la session est signalé, pas bloqué', async () => {
    const { r } = await openRoll({ sheet: () => sheetOf([LINE('a', 'Awa')], { outsideSessionDays: true }) });

    assert.equal(r.outsideDays, true);
    r.setPresence(byId(r, 'a'), true);
    assert.equal(r.canSave, true);
});

test('erreur de chargement de la feuille : message visible, pas de feuille fantôme', async () => {
    const { r } = await openRoll({ fail: (m, e) => (e.startsWith('/internat/attendance/sheet') ? apiError('FORBIDDEN', 403, 'Accès refusé') : undefined) });

    assert.deepEqual(plain(r.lines), []);
    assert.equal(r.hasSheet, false);
    assert.match(r.error, /Accès refusé/);
});

test('la Halqa vide : feuille vide, rien à enregistrer', async () => {
    const { r } = await openRoll({ lines: [] });

    assert.equal(r.hasSheet, true);
    assert.equal(r.lines.length, 0);
    assert.equal(r.canSave, false);
});
