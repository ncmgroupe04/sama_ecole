/**
 * Pointage par sessions : logique pure partagée (wwwroot/js/attendance-logic.js), onglet « Pointage » avec sessions et groupes
 * (internat-attendance.js) et configuration des sessions par le Directeur (attendance-sessions.js).
 *
 * Enjeux : (1) le chemin d'origine (école sans session : appel de nuit d'un pavillon) reste identique — il est couvert par
 * internat-attendance.test.mjs ; (2) une session des internes garde la feuille d'un pavillon (avec `sessionId`), les autres publics passent par
 * un groupe (classe ou Oustaz) et envoient des `studentId` ; (3) changer de session avec une saisie non enregistrée demande confirmation ;
 * (4) la Direction crée, modifie, active, ordonne et supprime des sessions sans jamais perdre le message du serveur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const TODAY = '2026-10-07'; // un mercredi

const NIGHT = { id: 's-night', name: 'Appel de nuit', audience: 'Interns', days: [1, 2, 3, 4, 5, 6, 7], timeLabel: '21 h 00', isActive: true, sortOrder: 0, hasRecords: true };
const LUNCH = { id: 's-lunch', name: 'Déjeuner', audience: 'AllBoarders', days: [1, 2, 3, 4, 5], timeLabel: null, isActive: true, sortOrder: 1, hasRecords: false };
const HALQA = { id: 's-halqa', name: 'Appel de la Halqa', audience: 'Halqa', days: [1, 2, 3, 4, 5, 6, 7], timeLabel: null, isActive: true, sortOrder: 2, hasRecords: false };

const GROUPS = [{ id: 'c1', name: 'CM2', studentCount: 6 }, { id: 'c2', name: '6ème', studentCount: 4 }];

const PAVILION_SHEET = (lines) => ({ date: TODAY, dormitoryId: 'd1', dormitoryName: 'Pavillon A', sessionId: 's-night', sessionName: 'Appel de nuit', outsideSessionDays: false, lines });
const GROUP_SHEET = (lines, extra = {}) => ({
    sessionId: 's-lunch', sessionName: 'Déjeuner', audience: 'AllBoarders', groupId: 'c1', groupName: 'CM2', date: TODAY, outsideSessionDays: false, lines, ...extra
});

const PLINE = (id, name, extra = {}) => ({ boarderId: id, studentName: name, matricule: `M-${id}`, roomName: 'Chambre 1', bedNumber: 1, onLeave: false, recorded: false, isPresent: null, note: null, ...extra });
const GLINE = (id, name, extra = {}) => ({ studentId: id, studentName: name, studentNameAr: null, matricule: `M-${id}`, detail: 'Interne', onLeave: false, recorded: false, isPresent: null, note: null, ...extra });

function apiError(code, status = 409, message = 'technique') {
    const error = new Error(message);
    error.code = code;
    error.status = status;
    return error;
}

function boot({ role = 'Directeur', sessions = [NIGHT, LUNCH, HALQA], groups = GROUPS, groupLines = [GLINE('a', 'Awa'), GLINE('b', 'Binta', { onLeave: true, isPresent: false, note: 'En permission' })],
    fail = () => undefined, post } = {}) {
    const calls = [];
    const previews = [];
    const events = [];
    const state = { sessions: structuredClone(sessions) };
    const record = async (method, endpoint, body, result) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        const error = fail(method, endpoint, body);
        if (error) throw error;
        return typeof result === 'function' ? result() : result;
    };
    const route = (endpoint) => {
        if (endpoint.startsWith('/boarding/attendance/sessions')) return () => structuredClone(state.sessions);
        if (endpoint === '/boarding/attendance/session-templates') return [{ code: 'lunch', name: 'Déjeuner', audience: 'AllBoarders', timeLabel: '12 h 30', description: 'x' }, { code: 'dinner', name: 'Dîner', audience: 'AllBoarders', timeLabel: '19 h 30', description: 'x' }];
        if (endpoint === '/boarding/dormitories') return [{ id: 'd1', name: 'Pavillon A' }, { id: 'd2', name: 'Pavillon B' }];
        if (endpoint.startsWith('/boarding/attendance/groups')) return groups;
        if (endpoint.startsWith('/boarding/attendance/sheet')) return GROUP_SHEET(groupLines);
        if (endpoint.startsWith('/boarding/attendance?')) return PAVILION_SHEET([PLINE('p1', 'Interne un'), PLINE('p2', 'Interne deux')]);
        if (endpoint.startsWith('/boarding/attendance/monthly-summary')) {
            return { monthStart: '2026-10-01', sessionId: 's-lunch', sessionName: 'Déjeuner', audience: 'AllBoarders', groupId: 'c1', groupName: 'CM2', days: ['2026-10-05', '2026-10-06'],
                rows: [{ studentId: 'a', studentName: 'Awa', matricule: 'M-a', detail: 'Interne', present: 2, absent: 0, onLeave: 0, notRecorded: 0 }, { studentId: 'b', studentName: 'Binta', matricule: 'M-b', detail: null, present: 0, absent: 1, onLeave: 1, notRecorded: 0 }] };
        }
        return null;
    };
    const defaultPost = (endpoint, body) => (!body || !body.entries ? {} : endpoint === '/boarding/attendance/sheet'
        ? { sessionId: body.sessionId, groupId: body.groupId, date: body.date, saved: body.entries.length, rejected: 0, lines: body.entries.map((e) => ({ studentId: e.studentId, outcome: 'Saved', code: null, message: null })) }
        : { date: body.date, dormitoryId: body.dormitoryId, saved: body.entries.length, rejected: 0, lines: body.entries.map((e) => ({ boarderId: e.boarderId, outcome: 'Saved', code: null, message: null })) });
    const ctx = loadScripts(['internat-helpers.js', 'attendance-logic.js', 'internat-attendance.js', 'attendance-sessions.js'], {
        preload: {
            auth: { role },
            pdfPreview: { state: () => ({ showPdfModal: false, openPdfPreview: async (...args) => { previews.push(args); } }) },
            api: {
                get: (endpoint) => record('GET', endpoint, undefined, route(endpoint)),
                post: (endpoint, body) => record('POST', endpoint, body, (post || defaultPost)(endpoint, plain(body))),
                put: (endpoint, body) => record('PUT', endpoint, body, null),
                delete: (endpoint) => record('DELETE', endpoint, undefined, null),
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : (err && err.message) || fallback)
            }
        }
    });
    ctx.window.internatHelpers.today = () => TODAY;
    ctx.window.dispatchEvent = (event) => { events.push(event.type); return true; };
    return { ctx, calls, previews, events, state, logic: ctx.window.attendanceLogic, tab: () => ctx.component('attendanceTab'), config: () => ctx.component('attendanceSessionsConfig') };
}

async function openGroupSheet(options) {
    const env = boot(options);
    const t = env.tab();
    await flush();
    t.sel.sessionId = 's-lunch';
    await t.onSessionChange();
    t.sel.groupId = 'c1';
    await t.onSelectionChange();
    return { ...env, t };
}

// ---------------------------------------------------------------- logique pure : groupes, jours, mois

test('toGroupLines : la clé de travail boarderId porte l\'identifiant de l\'élève, detail remplace chambre · lit', () => {
    const { logic } = boot();
    const lines = logic.toGroupLines(GROUP_SHEET([GLINE('a', 'Awa'), GLINE('b', 'Binta', { recorded: true, isPresent: true, note: 'RAS' })]));

    assert.equal(lines[0].boarderId, 'a');
    assert.equal(lines[0].studentId, 'a');
    assert.equal(lines[0].roomName, 'Interne');
    assert.deepEqual(plain(lines[1].base), { recorded: true, isPresent: true, note: 'RAS' });
});

test('dirtyGroupEntries : corps {studentId, isPresent, note}, seulement les lignes décidées et modifiées', () => {
    const { logic } = boot();
    const lines = logic.toGroupLines(GROUP_SHEET([GLINE('a', 'Awa'), GLINE('b', 'Binta'), GLINE('c', 'Cheikh', { recorded: true, isPresent: true })]));
    logic.setPresence(lines[0], true);
    lines[0].note = '  Retard ';

    assert.deepEqual(plain(logic.dirtyGroupEntries(lines)), [{ studentId: 'a', isPresent: true, note: 'Retard' }]);
});

test('applyResult : le serveur répond par studentId pour une feuille de groupe, par boarderId pour un pavillon', () => {
    const { logic } = boot();
    const lines = logic.toGroupLines(GROUP_SHEET([GLINE('a', 'Awa'), GLINE('b', 'Binta')]));
    logic.setPresence(lines[0], true);
    logic.setPresence(lines[1], true);

    logic.applyResult(lines, { saved: 1, rejected: 1, lines: [
        { studentId: 'a', outcome: 'Saved', code: null, message: null },
        { studentId: 'b', outcome: 'Rejected', code: 'NOT_IN_GROUP', message: 'x' }
    ] });

    assert.equal(lines[0].recorded, true);
    assert.equal(lines[1].rejection, logic.rejectionMessage('NOT_IN_GROUP', ''));
    assert.match(lines[1].rejection, /groupe/);
});

test('daysSummary : tous les jours, plage consécutive, liste, aucun', () => {
    const { logic } = boot();
    assert.equal(logic.daysSummary([1, 2, 3, 4, 5, 6, 7]), 'Tous les jours');
    assert.equal(logic.daysSummary([1, 2, 3, 4, 5]), 'Lun – Ven');
    assert.equal(logic.daysSummary([1, 3, 6]), 'Lun, Mer, Sam');
    assert.equal(logic.daysSummary([6, 7]), 'Sam, Dim', 'deux jours : liste, pas de plage');
    assert.equal(logic.daysSummary([]), 'Aucun jour');
    assert.equal(logic.daysSummary([3, 3, 1]), 'Lun, Mer', 'doublons ignorés, ordre normalisé');
});

test('isoDay et outsideDays : lundi = 1, dimanche = 7, un jour hors sélection est signalé', () => {
    const { logic } = boot();
    assert.equal(logic.isoDay('2026-10-05'), 1);
    assert.equal(logic.isoDay('2026-10-11'), 7);
    assert.equal(logic.isoDay('2026-10-07'), 3);

    assert.equal(logic.outsideDays([1, 2, 3, 4, 5], '2026-10-11'), true, 'dimanche hors lundi–vendredi');
    assert.equal(logic.outsideDays([1, 2, 3, 4, 5], '2026-10-07'), false);
    assert.equal(logic.outsideDays([], '2026-10-11'), false, 'sans jours connus : rien à signaler');
});

test('monthOf, monthLabel et summaryTotals', () => {
    const { logic } = boot();
    assert.equal(logic.monthOf('2026-10-07'), '2026-10');
    assert.equal(logic.monthLabel('2026-10'), 'octobre 2026');
    assert.equal(logic.monthLabel('2027-02'), 'février 2027');
    assert.equal(logic.monthLabel('nope'), '');
    assert.deepEqual(plain(logic.summaryTotals([{ present: 2, absent: 1, onLeave: 0, notRecorded: 3 }, { present: 1, absent: 0, onLeave: 2, notRecorded: 0 }])),
        { present: 3, absent: 1, onLeave: 2, notRecorded: 3 });
    assert.deepEqual(plain(logic.summaryTotals([])), { present: 0, absent: 0, onLeave: 0, notRecorded: 0 });
});

test('validateSession : nom, public, jours et repère horaire', () => {
    const { logic } = boot();
    const ok = { name: 'Dîner', audience: 'AllBoarders', days: [1], timeLabel: '19 h' };

    assert.equal(logic.validateSession(ok), '');
    assert.match(logic.validateSession({ ...ok, name: '  ' }), /nom/i);
    assert.match(logic.validateSession({ ...ok, name: 'x'.repeat(61) }), /60/);
    assert.match(logic.validateSession({ ...ok, audience: 'Tous' }), /public|s'adresse/i);
    assert.match(logic.validateSession({ ...ok, days: [] }), /jour/i);
    assert.match(logic.validateSession({ ...ok, timeLabel: 'h'.repeat(41) }), /40/);
});

test('audienceLabel, groupLabel et isGroupAudience', () => {
    const { logic } = boot();
    assert.equal(logic.groupLabel('Interns'), 'Pavillon');
    assert.equal(logic.groupLabel('AllBoarders'), 'Classe');
    assert.equal(logic.groupLabel('HalfBoarders'), 'Classe');
    assert.equal(logic.groupLabel('Halqa'), 'Oustaz');
    assert.equal(logic.isGroupAudience('Interns'), false);
    assert.equal(logic.isGroupAudience('Halqa'), true);
    assert.match(logic.audienceLabel('Halqa'), /Halqa/);
    assert.equal(logic.audienceLabel('???'), '');
});

test('helpers : registerPdfUrl et monthlySummaryUrl', () => {
    const { ctx } = boot();
    const h = ctx.window.internatHelpers;

    assert.equal(h.registerPdfUrl('d1', '2026-10-07'), '/api/v1/boarding/dormitories/d1/register-pdf?date=2026-10-07');
    assert.equal(h.registerPdfUrl('d1', '2026-10-07', 's1'), '/api/v1/boarding/dormitories/d1/register-pdf?date=2026-10-07&sessionId=s1');
    assert.equal(h.registerPdfUrl('d1', undefined, 's1'), '/api/v1/boarding/dormitories/d1/register-pdf?sessionId=s1');
    assert.equal(h.monthlySummaryUrl('2026-10', 's1', 'c1'), '/api/v1/boarding/attendance/monthly-summary?month=2026-10&sessionId=s1&groupId=c1');
    assert.equal(h.monthlySummaryUrl('2026-10', 's1', 'c1', true), '/api/v1/boarding/attendance/monthly-summary/pdf?month=2026-10&sessionId=s1&groupId=c1');
    assert.equal(h.monthlySummaryUrl('octobre', 's1', 'c1'), '', 'mois invalide : pas d\'URL');
    assert.equal(h.monthlySummaryUrl('2026-10', '', 'c1'), '', 'sans session : pas d\'URL');
});

// ---------------------------------------------------------------- onglet : sessions et groupes

test('à l\'ouverture : les sessions actives sont chargées et la première est choisie', async () => {
    const { tab, calls } = boot();
    const t = tab();
    await flush();

    assert.ok(calls.some((c) => c.endpoint === '/boarding/attendance/sessions?activeOnly=true'));
    assert.equal(t.sel.sessionId, 's-night');
    assert.equal(t.audience, 'Interns');
    assert.equal(t.isGroup, false);
    assert.deepEqual(plain(t.sessionOptions.map((o) => o.label)), ['Appel de nuit · 21 h 00', 'Déjeuner', 'Appel de la Halqa']);
});

test('une session des internes garde la feuille du pavillon et y ajoute sessionId', async () => {
    const { tab, calls } = boot();
    const t = tab();
    await flush();
    t.sel.dormitoryId = 'd1';
    await t.onSelectionChange();

    const call = calls.find((c) => c.endpoint.startsWith('/boarding/attendance?'));
    assert.match(call.endpoint, /dormitoryId=d1/);
    assert.match(call.endpoint, /sessionId=s-night/);
    assert.equal(t.loaded.audience, 'Interns');
    assert.equal(t.canPrintRegister, true);
    assert.equal(t.canSummarize, true);
});

test('un public de groupe : les groupes du serveur, puis la feuille GET attendance/sheet', async () => {
    const { t, calls } = await openGroupSheet();

    assert.ok(calls.some((c) => c.endpoint === '/boarding/attendance/groups?sessionId=s-lunch'));
    const sheet = calls.find((c) => c.endpoint.startsWith('/boarding/attendance/sheet'));
    assert.match(sheet.endpoint, /sessionId=s-lunch/);
    assert.match(sheet.endpoint, /groupId=c1/);
    assert.match(sheet.endpoint, /date=2026-10-07/);
    assert.equal(t.groupLabel, 'Classe');
    assert.equal(t.dayLabel, 'Jour');
    assert.equal(t.sheetName, 'CM2');
    assert.equal(t.lines.length, 2);
    assert.equal(t.canPrintRegister, false, 'pas de registre de pavillon pour une classe');
    assert.deepEqual(plain(t.groupOptions.map((o) => o.label)), ['CM2 (6)', '6ème (4)']);
});

test('un seul groupe : il se choisit tout seul et la feuille s\'ouvre', async () => {
    const env = boot({ groups: [GROUPS[0]] });
    const t = env.tab();
    await flush();
    t.sel.sessionId = 's-lunch';
    await t.onSessionChange();

    assert.equal(t.sel.groupId, 'c1');
    assert.equal(t.hasSheet, true);
});

test('l\'envoi d\'une feuille de groupe part vers attendance/sheet avec des studentId', async () => {
    const { t, calls } = await openGroupSheet();
    t.setPresence(t.lines.find((l) => l.boarderId === 'a'), true);
    await t.save();

    const post = calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/boarding/attendance/sheet');
    assert.equal(post.body.sessionId, 's-lunch');
    assert.equal(post.body.groupId, 'c1');
    assert.equal(post.body.date, TODAY);
    assert.deepEqual(post.body.entries.map((e) => e.studentId).sort(), ['a', 'b'], 'a décidée et b (permission pré-remplie)');
    assert.ok(post.body.entries.every((e) => e.boarderId === undefined));
    assert.match(t.notice, /2 lignes enregistrées/);
});

test('l\'envoi d\'une feuille de pavillon garde /boarding/attendance et ajoute sessionId', async () => {
    const env = boot();
    const t = env.tab();
    await flush();
    t.sel.dormitoryId = 'd1';
    await t.onSelectionChange();
    t.setPresence(t.lines[0], true);
    await t.save();

    const post = env.calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/boarding/attendance');
    assert.equal(post.body.sessionId, 's-night');
    assert.equal(post.body.dormitoryId, 'd1');
    assert.equal(post.body.entries[0].boarderId, 'p1');
});

test('une école sans session garde l\'appel de nuit d\'origine : pas de sessionId, et la session créée par le serveur est proposée ensuite', async () => {
    const env = boot({ sessions: [] });
    const t = env.tab();
    await flush();
    assert.equal(t.hasSessions, false);

    t.sel.dormitoryId = 'd1';
    await t.onSelectionChange();
    assert.doesNotMatch(env.calls.find((c) => c.endpoint.startsWith('/boarding/attendance?')).endpoint, /sessionId/);

    t.setPresence(t.lines[0], true);
    env.state.sessions = [NIGHT]; // le serveur vient de créer « Appel de nuit »
    await t.save();

    assert.equal(env.calls.find((c) => c.method === 'POST').body.sessionId, undefined);
    assert.equal(t.hasSessions, true, 'la session créée à la volée apparaît');
});

test('changer de session avec une saisie non enregistrée : confirmation, rien n\'est jeté', async () => {
    const { t, calls } = await openGroupSheet();
    t.setPresence(t.lines[0], true);
    const before = calls.length;

    t.sel.sessionId = 's-halqa';
    await t.onSessionChange();

    assert.equal(t.discard.open, true);
    assert.equal(calls.length, before, 'aucun appel avant confirmation');
    assert.equal(t.lines[0].isPresent, true);

    t.cancelDiscard();
    assert.equal(t.sel.sessionId, 's-lunch', 'la session de la feuille affichée est restaurée');
    assert.equal(t.sel.groupId, 'c1');
});

test('confirmer l\'abandon d\'un changement de session charge les groupes de la nouvelle session', async () => {
    const { t, calls } = await openGroupSheet();
    t.setPresence(t.lines[0], true);
    t.sel.sessionId = 's-halqa';
    await t.onSessionChange();
    await t.confirmDiscard();

    assert.equal(t.discard.open, false);
    assert.ok(calls.some((c) => c.endpoint === '/boarding/attendance/groups?sessionId=s-halqa'));
    assert.equal(t.hasSheet, false, 'le groupe est à choisir');
    assert.equal(t.sel.groupId, '');
});

test('passer d\'un public de groupe à celui des internes ramène la feuille du pavillon', async () => {
    const { t } = await openGroupSheet();
    t.sel.sessionId = 's-night';
    await t.onSessionChange();

    assert.equal(t.isGroup, false);
    assert.equal(t.hasSheet, false);
    assert.equal(t.currentGroupId, '');
});

test('jour hors des jours de la session : signalé par le serveur, jamais bloqué', async () => {
    const env = boot();
    const original = env.ctx.window.api.get;
    env.ctx.window.api.get = (endpoint) => (endpoint.startsWith('/boarding/attendance/sheet')
        ? original(endpoint).then((s) => ({ ...s, outsideSessionDays: true }))
        : original(endpoint));
    const t = env.tab();
    await flush();
    t.sel.sessionId = 's-lunch';
    await t.onSessionChange();
    t.sel.groupId = 'c1';
    await t.onSelectionChange();

    assert.equal(t.outsideDays, true);
    t.setPresence(t.lines[0], true);
    assert.equal(t.canSave, true, 'le pointage reste possible');
});

test('session désactivée entre-temps : message traduit à l\'enregistrement', async () => {
    const { t } = await openGroupSheet({ fail: (m, e) => (m === 'POST' ? apiError('ATTENDANCE_SESSION_INACTIVE', 409) : undefined) });
    t.setPresence(t.lines[0], true);
    await t.save();

    assert.match(t.error, /désactivée/);
    assert.equal(t.saving, false);
    assert.equal(t.lines[0].isPresent, true, 'la saisie n\'est pas perdue');
});

test('la liste des sessions se relit quand la Direction les modifie, sans jeter une saisie en cours', async () => {
    const { t, state } = await openGroupSheet();
    t.setPresence(t.lines[0], true);
    state.sessions = [LUNCH, { ...HALQA, name: 'Halqa du soir' }];

    await t.onSessionsChanged();

    assert.deepEqual(plain(t.sessions.map((s) => s.name)), ['Déjeuner', 'Halqa du soir']);
    assert.equal(t.lines[0].isPresent, true, 'la saisie en cours est conservée');
    assert.equal(t.hasSheet, true);
});

// ---------------------------------------------------------------- récapitulatif du mois

test('« Récapitulatif du mois » : mois de la feuille, GET monthly-summary pour le groupe affiché, totaux', async () => {
    const { t, calls } = await openGroupSheet();
    await t.openSummary();

    const call = calls.find((c) => c.endpoint.startsWith('/boarding/attendance/monthly-summary'));
    assert.equal(call.endpoint, '/boarding/attendance/monthly-summary?month=2026-10&sessionId=s-lunch&groupId=c1');
    assert.equal(t.summary.open, true);
    assert.deepEqual(plain(t.summaryTotals), { present: 2, absent: 1, onLeave: 1, notRecorded: 0 });
    assert.match(t.summaryTitle, /Déjeuner — CM2 — octobre 2026/);
});

test('récapitulatif d\'un pavillon : le groupe est le pavillon', async () => {
    const env = boot();
    const t = env.tab();
    await flush();
    t.sel.dormitoryId = 'd1';
    await t.onSelectionChange();
    await t.openSummary();

    assert.match(env.calls.find((c) => c.endpoint.startsWith('/boarding/attendance/monthly-summary')).endpoint, /sessionId=s-night&groupId=d1/);
});

test('changer de mois recharge le récapitulatif ; un mois vide est refusé sans appel', async () => {
    const { t, calls } = await openGroupSheet();
    await t.openSummary();
    const before = calls.length;

    t.summary.month = '2026-09';
    await t.loadSummary();
    assert.match(calls[calls.length - 1].endpoint, /month=2026-09/);

    t.summary.month = '';
    const count = calls.length;
    await t.loadSummary();
    assert.equal(calls.length, count, 'aucun appel sans mois');
    assert.match(t.summaryError, /mois/i);
    assert.ok(count > before);
});

test('le PDF du récapitulatif passe par la modale d\'aperçu commune', async () => {
    const { t, previews } = await openGroupSheet();
    await t.openSummary();
    await t.openSummaryPdf();

    assert.equal(previews.length, 1);
    assert.equal(previews[0][0], '/api/v1/boarding/attendance/monthly-summary/pdf?month=2026-10&sessionId=s-lunch&groupId=c1');
    assert.match(previews[0][2], /^Recapitulatif-Pointage-Dejeuner-CM2-2026-10\.pdf$/);
});

test('erreur du récapitulatif : message visible, pas de données fantômes', async () => {
    const { t } = await openGroupSheet({ fail: (m, e) => (e.startsWith('/boarding/attendance/monthly-summary') ? apiError('FORBIDDEN', 403, 'Accès refusé') : undefined) });
    await t.openSummary();

    assert.equal(t.summary.data, null);
    assert.match(t.summaryError, /Accès refusé/);
});

test('le registre PDF d\'un pavillon porte la session', async () => {
    const env = boot();
    const t = env.tab();
    await flush();
    t.sel.dormitoryId = 'd1';
    await t.onSelectionChange();
    await t.openRegisterPdf();

    assert.equal(env.previews[0][0], '/api/v1/boarding/dormitories/d1/register-pdf?date=2026-10-07&sessionId=s-night');
});

test('seul le Directeur voit « Configurer les sessions »', () => {
    assert.equal(boot({ role: 'Directeur' }).tab().canConfigure, true);
    assert.equal(boot({ role: 'Surveillant' }).tab().canConfigure, false);
    assert.equal(boot({ role: 'Secretariat' }).tab().canConfigure, false);
});

// ---------------------------------------------------------------- configuration des sessions

async function openConfig(options) {
    const env = boot(options);
    const c = env.config();
    await c.open();
    return { ...env, c };
}

test('ouvrir la configuration charge toutes les sessions et les modèles', async () => {
    const { c, calls } = await openConfig();

    assert.ok(calls.some((x) => x.endpoint === '/boarding/attendance/sessions'), 'toutes, pas seulement les actives');
    assert.ok(calls.some((x) => x.endpoint === '/boarding/attendance/session-templates'));
    assert.equal(c.sessions.length, 3);
    assert.equal(c.isOpen, true);
});

test('les modèles dont le nom est déjà pris ne sont plus proposés (casse ignorée)', async () => {
    const { c } = await openConfig({ sessions: [{ ...LUNCH, name: 'DÉJEUNER' }] });
    assert.deepEqual(plain(c.availableTemplates.map((t) => t.name)), ['Dîner']);
});

test('un modèle préremplit le formulaire, que la Direction peut modifier avant d\'enregistrer', async () => {
    const { c } = await openConfig();
    c.useTemplate({ code: 'dinner', name: 'Dîner', audience: 'AllBoarders', timeLabel: '19 h 30' });

    assert.equal(c.form.open, true);
    assert.equal(c.form.id, null);
    assert.equal(c.form.name, 'Dîner');
    assert.equal(c.form.audience, 'AllBoarders');
    assert.equal(c.form.timeLabel, '19 h 30');
    assert.deepEqual(plain(c.form.days), [1, 2, 3, 4, 5, 6, 7]);
});

test('créer une session : validation de confort, POST du corps nettoyé, rechargement et annonce', async () => {
    const { c, calls, events } = await openConfig();
    c.newSession();
    c.form.name = '   ';
    await c.saveForm();
    assert.match(c.form.error, /nom/i);
    assert.equal(calls.some((x) => x.method === 'POST'), false, 'refusé avant l\'appel');

    c.form.name = '  Goûter ';
    c.form.audience = 'AllBoarders';
    c.form.timeLabel = '  ';
    c.toggleDay(6);
    c.toggleDay(7);
    await c.saveForm();

    const post = calls.find((x) => x.method === 'POST');
    assert.equal(post.endpoint, '/boarding/attendance/sessions');
    assert.deepEqual(post.body, { name: 'Goûter', audience: 'AllBoarders', days: [1, 2, 3, 4, 5], timeLabel: null, isActive: true });
    assert.equal(c.form.open, false);
    assert.equal(c.notice, 'Session créée.');
    assert.ok(events.includes('attendance-sessions-changed'));
});

test('au moins un jour reste obligatoire', async () => {
    const { c } = await openConfig();
    c.newSession();
    c.form.name = 'X';
    for (const d of [1, 2, 3, 4, 5, 6, 7]) c.toggleDay(d);
    await c.saveForm();

    assert.match(c.form.error, /jour/i);
});

test('modifier une session : PUT vers son identifiant ; l\'audience est verrouillée dès qu\'elle porte des pointages', async () => {
    const { c, calls } = await openConfig();
    c.editSession(NIGHT);
    assert.equal(c.audienceLocked, true, 'des pointages existent');
    c.form.name = 'Appel du soir';
    await c.saveForm();

    const put = calls.find((x) => x.method === 'PUT');
    assert.equal(put.endpoint, '/boarding/attendance/sessions/s-night');
    assert.equal(put.body.name, 'Appel du soir');
    assert.equal(put.body.timeLabel, '21 h 00');

    c.editSession(LUNCH);
    assert.equal(c.audienceLocked, false, 'jamais pointée : l\'audience reste modifiable');
});

test('une erreur du serveur à l\'enregistrement reste dans le formulaire, ouvert', async () => {
    const { c } = await openConfig({ fail: (m) => (m === 'POST' ? apiError('VALIDATION_ERROR', 422, 'Une session porte déjà ce nom.') : undefined) });
    c.newSession();
    c.form.name = 'Déjeuner';
    await c.saveForm();

    assert.equal(c.form.open, true);
    assert.match(c.form.error, /déjà ce nom/);
    assert.equal(c.form.saving, false);
});

test('activer ou désactiver : PUT avec le corps de la session et isActive inversé', async () => {
    const { c, calls, events } = await openConfig();
    await c.toggleActive(LUNCH);

    const put = calls.find((x) => x.method === 'PUT');
    assert.equal(put.endpoint, '/boarding/attendance/sessions/s-lunch');
    assert.equal(put.body.isActive, false);
    assert.deepEqual(plain(put.body.days), [1, 2, 3, 4, 5]);
    assert.ok(events.includes('attendance-sessions-changed'));
});

test('réordonner : la liste COMPLÈTE des identifiants dans le nouvel ordre ; les bords ne bougent pas', async () => {
    const { c, calls } = await openConfig();

    assert.equal(c.reordered(c.sessions[0], -1), null, 'la première ne monte pas');
    assert.equal(c.reordered(c.sessions[2], 1), null, 'la dernière ne descend pas');
    assert.deepEqual(plain(c.reordered(c.sessions[2], -1)), ['s-night', 's-halqa', 's-lunch']);

    await c.move(c.sessions[0], 1);
    const put = calls.find((x) => x.endpoint === '/boarding/attendance/sessions/order');
    assert.deepEqual(put.body.orderedIds, ['s-lunch', 's-night', 's-halqa']);
});

test('supprimer : confirmation, DELETE, rechargement ; une session pointée est refusée avec un message clair', async () => {
    const { c, calls } = await openConfig({ fail: (m, e) => (m === 'DELETE' && e.endsWith('s-night') ? apiError('RESOURCE_IN_USE', 409) : undefined) });

    c.askDelete(NIGHT);
    await c.confirmDelete();
    assert.equal(c.removal.open, true, 'la fenêtre reste ouverte');
    assert.match(c.removal.error, /désactivez-la/);

    c.askDelete(LUNCH);
    await c.confirmDelete();
    assert.ok(calls.some((x) => x.method === 'DELETE' && x.endpoint === '/boarding/attendance/sessions/s-lunch'));
    assert.equal(c.removal.open, false);
    assert.equal(c.notice, 'Session supprimée.');
});

test('échec du chargement de la configuration : signalé', async () => {
    const env = boot({ fail: (m, e) => (e === '/boarding/attendance/sessions' ? apiError('FORBIDDEN', 403, 'Accès refusé') : undefined) });
    const c = env.config();
    await c.open();

    assert.match(c.error, /Accès refusé/);
    assert.equal(c.loading, false);
});
