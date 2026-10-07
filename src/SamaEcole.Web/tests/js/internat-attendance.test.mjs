/**
 * Onglet « Pointage » de l'écran Internat (wwwroot/js/internat-attendance.js) — lot G4, tâche 11.
 *
 * Enjeux : (1) un pensionnaire en permission ne peut JAMAIS être coché présent (le serveur le refuserait : BOARDER_ON_LEAVE) ; (2) seules les lignes
 * décidées ET modifiées partent — une ligne déjà enregistrée n'est pas renvoyée ; (3) un résultat mixte montre les rejets sans masquer les succès ;
 * (4) une date future est refusée avant tout appel ; (5) changer de pavillon ou de nuit ne jette pas une saisie non enregistrée sans le dire.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const TODAY = '2026-10-07';
const D1 = 'd1';

const LINE = (id, name, bed, extra = {}) => ({
    boarderId: id, studentName: name, matricule: `M-${id}`, roomName: 'Chambre 101', bedNumber: bed,
    onLeave: false, recorded: false, isPresent: null, note: null, ...extra
});

const SHEET = (lines) => ({ date: TODAY, dormitoryId: D1, dormitoryName: 'Pavillon Oustaz Ahmad', lines });

const DEFAULT_LINES = () => [
    LINE('a', 'Awa Diop', 1),
    LINE('b', 'Binta Fall', 2),
    LINE('c', 'Cheikh Ba', 3, { onLeave: true, isPresent: false, note: 'En permission' }),
    LINE('d', 'Daba Sy', 4, { recorded: true, isPresent: true })
];

function apiError(code, status = 409, message = 'technical') {
    const error = new Error(message);
    error.code = code;
    error.status = status;
    return error;
}

function boot({ role = 'Directeur', lines = DEFAULT_LINES(), dormitories = [{ id: D1, name: 'Pavillon Oustaz Ahmad' }, { id: 'd2', name: 'Pavillon Fatima' }],
    fail = () => undefined, postResult } = {}) {
    const calls = [];
    const previews = [];
    const record = async (method, endpoint, body, result) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        const error = fail(method, endpoint);
        if (error) throw error;
        return result;
    };
    const route = (endpoint) => {
        if (endpoint === '/boarding/dormitories') return dormitories;
        if (endpoint.startsWith('/boarding/attendance?')) return SHEET(lines);
        return null;
    };
    const defaultPost = (body) => ({
        date: body.date, dormitoryId: body.dormitoryId, saved: body.entries.length, rejected: 0,
        lines: body.entries.map((e) => ({ boarderId: e.boarderId, outcome: 'Saved', code: null, message: null }))
    });
    const ctx = loadScripts(['internat-helpers.js', 'internat-attendance.js'], {
        preload: {
            auth: { role },
            pdfPreview: { state: () => ({ showPdfModal: false, openPdfPreview: async (...args) => { previews.push(args); } }) },
            api: {
                get: (endpoint) => record('GET', endpoint, undefined, route(endpoint)),
                post: (endpoint, body) => record('POST', endpoint, body, (postResult || defaultPost)(plain(body))),
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : (err && err.message) || fallback)
            }
        }
    });
    ctx.window.internatHelpers.today = () => TODAY;
    return { ctx, calls, previews, tab: () => ctx.component('attendanceTab'), logic: ctx.window.attendanceLogic, h: ctx.window.internatHelpers };
}

async function withSheet(options) {
    const ctx = boot(options);
    const t = ctx.tab();
    await flush();
    t.sel.dormitoryId = D1;
    await t.onSelectionChange();
    return { ...ctx, t };
}

const byId = (t, id) => t.lines.find((l) => l.boarderId === id);

// ---------------------------------------------------------------- logique pure

test('setPresence : un pensionnaire en permission est verrouillé « absent »', () => {
    const { logic } = boot();
    const away = logic.toLines(SHEET(DEFAULT_LINES())).find((l) => l.boarderId === 'c');

    assert.equal(logic.setPresence(away, true), false, 'refusé');
    assert.equal(away.isPresent, false);
    assert.equal(logic.setPresence(away, false), true);
    assert.equal(away.isPresent, false);
});

test('setPresence : un pensionnaire ordinaire se pointe présent ou absent', () => {
    const { logic } = boot();
    const line = logic.toLines(SHEET(DEFAULT_LINES())).find((l) => l.boarderId === 'a');
    assert.equal(logic.setPresence(line, true), true);
    assert.equal(line.isPresent, true);
    assert.equal(logic.setPresence(line, false), true);
    assert.equal(line.isPresent, false);
});

test('isDirty : décidée ET (jamais enregistrée OU modifiée) ; une ligne enregistrée et inchangée ne l\'est pas', () => {
    const { logic } = boot();
    const lines = logic.toLines(SHEET(DEFAULT_LINES()));
    const [a, , c, d] = lines;

    assert.equal(logic.isDirty(a), false, 'non décidée');
    logic.setPresence(a, true);
    assert.equal(logic.isDirty(a), true, 'décidée, jamais enregistrée');

    assert.equal(logic.isDirty(d), false, 'déjà enregistrée, inchangée : pas renvoyée');
    logic.setPresence(d, false);
    assert.equal(logic.isDirty(d), true, 'modifiée');
    logic.setPresence(d, true);
    assert.equal(logic.isDirty(d), false, 'revenue à sa valeur enregistrée');

    assert.equal(logic.isDirty(c), true, 'absente pré-remplie « En permission » mais pas encore enregistrée : à envoyer');
});

test('isDirty : une note modifiée sur une ligne enregistrée la rend à renvoyer ; espaces et vide ignorés', () => {
    const { logic } = boot();
    const d = logic.toLines(SHEET(DEFAULT_LINES())).find((l) => l.boarderId === 'd');
    d.note = '   ';
    assert.equal(logic.isDirty(d), false, 'note blanche = pas de note');
    d.note = 'Mal de tête';
    assert.equal(logic.isDirty(d), true);
});

test('dirtyEntries : corps minimal {boarderId, isPresent, note}, note blanche en null', () => {
    const { logic } = boot();
    const lines = logic.toLines(SHEET(DEFAULT_LINES()));
    logic.setPresence(lines[0], true);
    logic.setPresence(lines[1], false);
    lines[1].note = '  Malade ';

    assert.deepEqual(plain(logic.dirtyEntries(lines)), [
        { boarderId: 'a', isPresent: true, note: null },
        { boarderId: 'b', isPresent: false, note: 'Malade' },
        { boarderId: 'c', isPresent: false, note: 'En permission' }
    ]);
});

test("hasUnsavedInput : une permission pré-remplie n'est PAS une saisie ; une décision de l'utilisateur l'est", () => {
    const { logic } = boot();
    const lines = logic.toLines(SHEET(DEFAULT_LINES()));

    assert.equal(logic.counts(lines).dirty, 1, "la permission est à envoyer dès l'ouverture");
    assert.equal(logic.hasUnsavedInput(lines), false, "mais personne n'a rien saisi");

    logic.setPresence(lines[0], true);
    assert.equal(logic.hasUnsavedInput(lines), true);
    logic.applyResult(lines, { saved: 1, rejected: 0, lines: [{ boarderId: 'a', outcome: 'Saved', code: null, message: null }] });
    assert.equal(logic.hasUnsavedInput(lines), false, 'enregistrée : plus de saisie en attente');

    lines[3].note = 'Mal de tête';
    assert.equal(logic.hasUnsavedInput(lines), true, 'une note ajoutée compte');
});

test('markAllPresent : ne touche ni les lignes déjà décidées ni les permissions', () => {
    const { logic } = boot();
    const lines = logic.toLines(SHEET(DEFAULT_LINES()));
    logic.setPresence(lines[1], false);
    logic.markAllPresent(lines);

    assert.equal(lines[0].isPresent, true);
    assert.equal(lines[1].isPresent, false, 'décision déjà prise : conservée');
    assert.equal(lines[2].isPresent, false, 'permission : reste absent');
    assert.equal(lines[3].isPresent, true);
});

test('validateDate : obligatoire, jamais dans le futur', () => {
    const { logic } = boot();
    assert.equal(logic.validateDate(TODAY, TODAY), '');
    assert.equal(logic.validateDate('2026-10-06', TODAY), '');
    assert.match(logic.validateDate('2026-10-08', TODAY), /future/);
    assert.match(logic.validateDate('', TODAY), /date/i);
});

test('validateNotes : 300 caractères au plus par note', () => {
    const { logic } = boot();
    const lines = logic.toLines(SHEET(DEFAULT_LINES()));
    assert.equal(logic.validateNotes(lines), '');
    lines[0].note = 'x'.repeat(301);
    assert.match(logic.validateNotes(lines), /300/);
});

test('rejectionMessage : codes du serveur en messages lisibles, repli sur le texte du serveur', () => {
    const { logic } = boot();
    for (const code of ['NOT_IN_DORMITORY', 'DUPLICATE_ENTRY', 'BOARDER_ON_LEAVE']) {
        const message = logic.rejectionMessage(code, 'technique');
        assert.notEqual(message, 'technique', code);
        assert.ok(message.length > 10, code);
    }
    assert.equal(logic.rejectionMessage('AUTRE', 'texte serveur'), 'texte serveur');
    assert.ok(logic.rejectionMessage('AUTRE', '').length > 0, 'jamais un message vide');
});

test('applyResult : succès = ligne enregistrée et propre ; rejet = message conservé, ligne toujours à renvoyer', () => {
    const { logic } = boot();
    const lines = logic.toLines(SHEET(DEFAULT_LINES()));
    logic.setPresence(lines[0], true);
    logic.setPresence(lines[1], true);

    logic.applyResult(lines, {
        saved: 1, rejected: 1,
        lines: [
            { boarderId: 'a', outcome: 'Saved', code: null, message: null },
            { boarderId: 'b', outcome: 'Rejected', code: 'NOT_IN_DORMITORY', message: 'Ce pensionnaire n\'a pas de lit dans ce pavillon.' }
        ]
    });

    assert.equal(lines[0].recorded, true);
    assert.equal(logic.isDirty(lines[0]), false, 'enregistrée : plus à envoyer');
    assert.equal(lines[0].rejection, '');
    assert.match(lines[1].rejection, /lit/);
    assert.equal(logic.isDirty(lines[1]), true, 'rejetée : reste à traiter');
});

test('counts : présents, absents, à pointer', () => {
    const { logic } = boot();
    const lines = logic.toLines(SHEET(DEFAULT_LINES()));
    logic.setPresence(lines[0], true);
    assert.deepEqual(plain(logic.counts(lines)), { present: 2, absent: 1, pending: 1, dirty: 2 });
});

// ---------------------------------------------------------------- composant : feuille d'appel

test('choisir un pavillon et une nuit charge GET /boarding/attendance?dormitoryId=&date=', async () => {
    const { calls, t } = await withSheet();
    const call = calls.find((c) => c.endpoint.startsWith('/boarding/attendance?'));

    assert.ok(call);
    assert.match(call.endpoint, /dormitoryId=d1/);
    assert.match(call.endpoint, /date=2026-10-07/);
    assert.ok(calls.every((c) => !c.endpoint.startsWith('/internat')));
    assert.equal(t.lines.length, 4);
});

test("la nuit par défaut est aujourd'hui ; un seul pavillon se sélectionne tout seul", async () => {
    const { tab, calls } = boot({ dormitories: [{ id: D1, name: 'Pavillon Oustaz Ahmad' }] });
    const t = tab();
    await flush();

    assert.equal(t.sel.date, TODAY);
    assert.equal(t.sel.dormitoryId, D1);
    assert.ok(calls.some((c) => c.endpoint.startsWith('/boarding/attendance?')));
});

test('plusieurs pavillons : rien n\'est chargé avant le choix', async () => {
    const { tab, calls } = boot();
    const t = tab();
    await flush();

    assert.equal(t.sel.dormitoryId, '');
    assert.equal(calls.some((c) => c.endpoint.startsWith('/boarding/attendance?')), false);
    assert.equal(t.hasSheet, false);
});

test('date future : refusée avant tout appel', async () => {
    const { t, calls } = await withSheet();
    const before = calls.length;
    t.sel.date = '2026-10-08';
    await t.onSelectionChange();

    assert.equal(calls.length, before);
    assert.match(t.dateError, /future/);
});

test('seules les lignes modifiées partent, avec la date et le pavillon de la feuille chargée', async () => {
    const { t, calls } = await withSheet();
    t.setPresence(byId(t, 'a'), true);
    await t.save();

    const post = calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/boarding/attendance');
    assert.equal(post.body.date, TODAY);
    assert.equal(post.body.dormitoryId, D1);
    assert.deepEqual(post.body.entries.map((e) => e.boarderId).sort(), ['a', 'c'], 'a (décidée) et c (permission pré-remplie) ; d déjà enregistrée n\'est pas renvoyée');
});

test('un pensionnaire en permission ne peut pas être coché présent via le composant', async () => {
    const { t } = await withSheet();
    t.setPresence(byId(t, 'c'), true);
    assert.equal(byId(t, 'c').isPresent, false);
});

test('rien à enregistrer : aucun appel, message clair', async () => {
    const { t, calls } = await withSheet({ lines: [LINE('d', 'Daba Sy', 4, { recorded: true, isPresent: true })] });
    await t.save();

    assert.equal(calls.some((c) => c.method === 'POST'), false);
    assert.match(t.notice, /Aucune modification/);
});

test('résultat mixte : les rejets sont affichés ligne par ligne sans masquer les succès', async () => {
    const postResult = (body) => ({
        date: body.date, dormitoryId: body.dormitoryId, saved: 1, rejected: 1,
        lines: [
            { boarderId: 'a', outcome: 'Saved', code: null, message: null },
            { boarderId: 'c', outcome: 'Rejected', code: 'BOARDER_ON_LEAVE', message: 'Ce pensionnaire est en sortie cette nuit-là.' }
        ]
    });
    const { t } = await withSheet({ postResult });
    t.setPresence(byId(t, 'a'), true);
    await t.save();

    assert.equal(t.result.saved, 1);
    assert.equal(t.result.rejected, 1);
    assert.equal(byId(t, 'a').recorded, true);
    assert.match(byId(t, 'c').rejection, /sortie/);
    assert.equal(t.rejectedLines.length, 1);
    assert.match(t.notice, /1 ligne enregistrée/);
    assert.match(t.notice, /1 refusée/);
});

test('tout enregistré : message de succès, plus rien à envoyer', async () => {
    const { t, calls } = await withSheet();
    t.setPresence(byId(t, 'a'), true);
    await t.save();

    assert.equal(t.counts.dirty, 0);
    assert.match(t.notice, /enregistrée/);
    const before = calls.filter((c) => c.method === 'POST').length;
    await t.save();
    assert.equal(calls.filter((c) => c.method === 'POST').length, before, 'un second clic ne renvoie rien');
});

test('ATTENDANCE_CONFLICT : message traduit et feuille rechargée', async () => {
    let fail = true;
    const { t, calls } = await withSheet({ fail: (m) => (m === 'POST' && fail ? apiError('ATTENDANCE_CONFLICT') : undefined) });
    const before = calls.filter((c) => c.endpoint.startsWith('/boarding/attendance?')).length;
    t.setPresence(byId(t, 'a'), true);
    await t.save();
    fail = false;

    assert.match(t.error, /modifié par une autre personne/);
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/boarding/attendance?')).length, before + 1, 'feuille relue');
    assert.equal(t.saving, false);
});

test('422 de date hors année scolaire : message du serveur affiché, saisie conservée', async () => {
    const { t } = await withSheet({ fail: (m) => (m === 'POST' ? apiError('VALIDATION_ERROR', 422, "La date doit appartenir à l'année scolaire active.") : undefined) });
    t.setPresence(byId(t, 'a'), true);
    await t.save();

    assert.match(t.error, /année scolaire active/);
    assert.equal(byId(t, 'a').isPresent, true, 'rien n\'est perdu');
    assert.equal(t.saving, false);
});

test('« Tout présent » coche les lignes non décidées uniquement', async () => {
    const { t } = await withSheet();
    t.markAllPresent();

    assert.equal(byId(t, 'a').isPresent, true);
    assert.equal(byId(t, 'b').isPresent, true);
    assert.equal(byId(t, 'c').isPresent, false, 'permission');
});

// ---------------------------------------------------------------- saisie non enregistrée

test('changer de pavillon avec une saisie non enregistrée : demande de confirmation, rien n\'est jeté', async () => {
    const { t, calls } = await withSheet();
    t.setPresence(byId(t, 'a'), true);
    const before = calls.filter((c) => c.endpoint.startsWith('/boarding/attendance?')).length;

    t.sel.dormitoryId = 'd2';
    await t.onSelectionChange();

    assert.equal(t.discard.open, true);
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/boarding/attendance?')).length, before, 'pas de rechargement avant confirmation');
    assert.equal(byId(t, 'a').isPresent, true);
});

test('annuler la confirmation restaure le pavillon et la nuit de la feuille affichée', async () => {
    const { t } = await withSheet();
    t.setPresence(byId(t, 'a'), true);
    t.sel.dormitoryId = 'd2';
    t.sel.date = '2026-10-05';
    await t.onSelectionChange();
    t.cancelDiscard();

    assert.equal(t.discard.open, false);
    assert.equal(t.sel.dormitoryId, D1);
    assert.equal(t.sel.date, TODAY);
    assert.equal(byId(t, 'a').isPresent, true);
});

test('confirmer l\'abandon charge la nouvelle feuille', async () => {
    const { t, calls } = await withSheet();
    t.setPresence(byId(t, 'a'), true);
    t.sel.dormitoryId = 'd2';
    await t.onSelectionChange();
    await t.confirmDiscard();

    assert.equal(t.discard.open, false);
    assert.ok(calls.filter((c) => c.endpoint.includes('dormitoryId=d2')).length >= 1);
});

test('sans saisie non enregistrée, le changement est immédiat', async () => {
    const { t, calls } = await withSheet();
    t.sel.dormitoryId = 'd2';
    await t.onSelectionChange();

    assert.equal(t.discard.open, false);
    assert.ok(calls.some((c) => c.endpoint.includes('dormitoryId=d2')));
});

// ---------------------------------------------------------------- erreurs et PDF

test('erreur de chargement de la feuille : message visible, pas de feuille fantôme', async () => {
    const { t } = await withSheet({ fail: (m, e) => (e.startsWith('/boarding/attendance?') ? apiError('MODULE_DISABLED', 403) : undefined) });
    assert.match(t.error, /Internat/);
    assert.deepEqual(plain(t.lines), []);
});

test('échec du chargement des pavillons : signalé', async () => {
    const { tab } = boot({ fail: (m, e) => (e === '/boarding/dormitories' ? Object.assign(new Error(''), { status: 500 }) : undefined) });
    const t = tab();
    await flush();
    assert.match(t.error, /pavillons/i);
});

test('« Registre PDF » ouvre la modale commune sur la route du pavillon avec ?date= (yyyy-MM-dd)', async () => {
    const { t, previews } = await withSheet();
    await t.openRegisterPdf();

    assert.equal(previews.length, 1);
    const [url, title, fileName] = previews[0];
    assert.equal(url, `/api/v1/boarding/dormitories/${D1}/register-pdf?date=2026-10-07`);
    assert.match(title, /Pavillon Oustaz Ahmad/);
    assert.equal(fileName, 'Registre-Pavillon-Pavillon-Oustaz-Ahmad-2026-10-07.pdf');
});

test('« Registre PDF » sans pavillon choisi : aucune impression', async () => {
    const { tab, previews } = boot();
    const t = tab();
    await flush();
    await t.openRegisterPdf();
    assert.equal(previews.length, 0);
});
