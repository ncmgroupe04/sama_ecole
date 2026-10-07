/**
 * Onglet « Sorties » de l'écran Internat (wwwroot/js/internat-leaves.js) — lot G4, tâche 10.
 *
 * Enjeux : (1) la règle « accompagnateur habilité » est la MÊME que CompanionMatcher côté serveur (parité testée cas par cas avec
 * CompanionMatcherTests) ; (2) un accompagnateur hors liste est un AVERTISSEMENT, jamais un blocage (spec Q5 : un blocage serait dangereux lors d'une
 * urgence de santé) ; (3) le serveur reste le juge de `isCompanionUnlisted` — l'écran ne l'envoie pas ; (4) le retour porte le `rowVersion` de la sortie.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const TODAY = '2026-10-07';

const LEAVE = (id, extra = {}) => ({
    id, boarderId: 's1', studentId: 'st1', studentName: 'Awa Diop', matricule: 'M-1', dormitoryName: 'Pavillon Fatima', roomName: 'Chambre 101',
    reason: 'Weekend', reasonDetail: null, leaveDate: '2026-10-03', expectedReturnDate: '2026-10-05', actualReturnDate: null,
    accompaniedBy: 'Fatou Diop', isCompanionUnlisted: false, status: 'Active', rowVersion: 21, ...extra
});

const BOARDER = { id: 's1', studentName: 'Awa Diop', matricule: 'M-1', classroomName: 'CM2', regime: 'Interne', isActive: true, bedId: 'b1' };
const DETAIL = {
    boarder: BOARDER, medicalNotes: 'Asthme', emergencyContactName: 'Fatou', emergencyContactPhone: '77',
    allowedExitPersons: [{ name: 'Fatou Diop', relationship: 'Mère', phone: '77' }], leaves: []
};

function apiError(code, status = 409) {
    const error = new Error('technical');
    error.code = code;
    error.status = status;
    return error;
}

function boot({ role = 'Directeur', fail = () => undefined, list = { items: [LEAVE('l1'), LEAVE('l2', { status: 'Overdue', isCompanionUnlisted: true })], totalCount: 2, page: 1, pageSize: 20 } } = {}) {
    const calls = [];
    const record = async (method, endpoint, body, result) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        const error = fail(method, endpoint);
        if (error) throw error;
        return result;
    };
    const route = (endpoint) => {
        if (endpoint === '/boarding/dormitories') return [{ id: 'd1', name: 'Pavillon Fatima' }];
        if (endpoint.startsWith('/boarding/leaves?')) return list;
        if (endpoint.startsWith('/boarding/boarders?')) return { items: [BOARDER], totalCount: 1, page: 1, pageSize: 10 };
        if (endpoint === '/boarding/boarders/s1') return DETAIL;
        return null;
    };
    const ctx = loadScripts(['internat-helpers.js', 'internat-leaves.js'], {
        preload: {
            auth: { role },
            api: {
                get: (endpoint) => record('GET', endpoint, undefined, route(endpoint)),
                post: (endpoint, body) => record('POST', endpoint, body, LEAVE('new')),
                put: (endpoint, body) => record('PUT', endpoint, body, LEAVE('l1', { status: 'Returned', actualReturnDate: TODAY, rowVersion: 22 })),
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : (err && err.message) || fallback)
            }
        }
    });
    // La date du jour est celle de l'horloge du bac à sable : on la fige pour des assertions stables.
    ctx.window.internatHelpers.today = () => TODAY;
    return { ctx, calls, tab: () => ctx.component('leavesTab'), logic: ctx.window.leavesLogic, h: ctx.window.internatHelpers };
}

const ready = async (tab) => { await flush(); return tab; };

// ---------------------------------------------------------------- parité de la règle « accompagnateur habilité »

test('isCompanionListed : parité avec CompanionMatcherTests (espaces et casse ignorés, nom complet)', () => {
    const { logic } = boot();
    const persons = [{ name: 'Fatou Diop', relationship: 'Mère', phone: '770000000' }];

    assert.equal(logic.isCompanionListed('Fatou Diop', persons), true);
    assert.equal(logic.isCompanionListed('  fatou   DIOP ', persons), true);
    assert.equal(logic.isCompanionListed('Moussa', persons), false);
    assert.equal(logic.isCompanionListed('Fatou', persons), false, 'pas de correspondance partielle');
    assert.equal(logic.isCompanionListed('   ', persons), false);
    assert.equal(logic.isCompanionListed('Fatou Diop', []), false, 'liste vide : personne n\'est habilité');
    assert.equal(logic.isCompanionListed('Fatou Diop', undefined), false);
});

test("isCompanionListed : les accents comptent (Élodie ≠ Elodie) comme côté serveur, la casse non (É = é)", () => {
    const { logic } = boot();
    assert.equal(logic.isCompanionListed('élodie sy', [{ name: 'Élodie Sy' }]), true);
    assert.equal(logic.isCompanionListed('Elodie Sy', [{ name: 'Élodie Sy' }]), false);
});

// ---------------------------------------------------------------- libellés

test('statuts et motifs affichés en français', () => {
    const { h } = boot();
    assert.equal(h.leaveStatusLabel('Pending'), 'À venir');
    assert.equal(h.leaveStatusLabel('Active'), 'En cours');
    assert.equal(h.leaveStatusLabel('Overdue'), 'En retard');
    assert.equal(h.leaveStatusLabel('Returned'), 'Rentré');
    assert.equal(h.leaveReasonLabel('Weekend'), 'Week-end');
    assert.equal(h.leaveReasonLabel('Sante'), 'Santé');
    assert.equal(h.leaveReasonLabel('Famille'), 'Famille');
    assert.equal(h.leaveReasonLabel('Autre'), 'Autre');
    assert.equal(h.leaveStatusLabel('Inconnu'), 'Inconnu', 'un statut inconnu reste lisible');
});

test('codes métier des sorties traduits (LEAVE_IN_PROGRESS garde le message précis du serveur)', () => {
    const { h } = boot();
    for (const code of ['LEAVE_ALREADY_OPEN', 'LEAVE_ALREADY_RETURNED', 'ATTENDANCE_CONFLICT']) {
        const message = h.errorMessage({ code, message: 'technique' }, 'repli');
        assert.notEqual(message, 'technique', code);
        assert.ok(message.length > 10, code);
    }
    assert.equal(h.errorMessage({ code: 'LEAVE_IN_PROGRESS', message: "Enregistrez d'abord le retour." }, 'repli'), "Enregistrez d'abord le retour.");
});

// ---------------------------------------------------------------- corps de requête

test('buildLeaveListQuery : filtres vides omis, unlistedOnly seulement quand coché', () => {
    const { logic } = boot();
    assert.equal(logic.buildLeaveListQuery({ status: '', dormitoryId: '', from: '', to: '', unlistedOnly: false }, 1, 20), 'page=1&pageSize=20');
    assert.equal(
        logic.buildLeaveListQuery({ status: 'Overdue', dormitoryId: 'd1', from: '2026-10-01', to: '2026-10-31', unlistedOnly: true }, 2, 20),
        'dormitoryId=d1&status=Overdue&from=2026-10-01&to=2026-10-31&unlistedOnly=true&page=2&pageSize=20');
});

test("buildDeclareBody : n'envoie JAMAIS isCompanionUnlisted (figé par le serveur), champs nettoyés", () => {
    const { logic } = boot();
    const body = logic.buildDeclareBody(
        { leaveDate: '2026-10-03', expectedReturnDate: '2026-10-05', reason: 'Famille', reasonDetail: '  ', accompaniedBy: '  Moussa Ba ' }, 's1');

    assert.deepEqual(plain(body), {
        boarderId: 's1', leaveDate: '2026-10-03', expectedReturnDate: '2026-10-05', reason: 'Famille', reasonDetail: null, accompaniedBy: 'Moussa Ba'
    });
    assert.equal('isCompanionUnlisted' in body, false);
});

test('validateDeclare : pensionnaire, dates cohérentes, accompagnateur obligatoire', () => {
    const { logic } = boot();
    const ok = { leaveDate: '2026-10-03', expectedReturnDate: '2026-10-03', reason: 'Weekend', reasonDetail: '', accompaniedBy: 'Fatou Diop' };

    assert.deepEqual(plain(logic.validateDeclare(ok, 's1')), {});
    assert.ok(logic.validateDeclare(ok, null).boarder);
    assert.ok(logic.validateDeclare({ ...ok, expectedReturnDate: '2026-10-02' }, 's1').expectedReturnDate, 'retour avant la sortie');
    assert.ok(logic.validateDeclare({ ...ok, leaveDate: '' }, 's1').leaveDate);
    assert.ok(logic.validateDeclare({ ...ok, accompaniedBy: '  ' }, 's1').accompaniedBy);
    assert.ok(logic.validateDeclare({ ...ok, accompaniedBy: 'x'.repeat(151) }, 's1').accompaniedBy);
    assert.ok(logic.validateDeclare({ ...ok, reasonDetail: 'x'.repeat(501) }, 's1').reasonDetail);
});

test('buildReturnBody : rowVersion de la sortie (0 valide) et date de retour', () => {
    const { logic } = boot();
    assert.deepEqual(plain(logic.buildReturnBody({ rowVersion: 0 }, '2026-10-06')), { rowVersion: 0, actualReturnDate: '2026-10-06' });
});

// ---------------------------------------------------------------- composant : registre

test('le registre charge /boarding/leaves, jamais une route /internat/*', async () => {
    const { calls, tab } = boot();
    const t = await ready(tab());

    const listCall = calls.find((c) => c.endpoint.startsWith('/boarding/leaves?'));
    assert.ok(listCall);
    assert.ok(calls.every((c) => !c.endpoint.startsWith('/internat')));
    assert.equal(t.items.length, 2);
});

test('filtre « accompagnateur non habilité » et statut « En retard » envoyés au serveur, retour en page 1', async () => {
    const { calls, tab } = boot();
    const t = await ready(tab());
    t.page = 4;
    t.filters.unlistedOnly = true;
    t.filters.status = 'Overdue';
    await t.applyFilters();

    const last = calls.filter((c) => c.endpoint.startsWith('/boarding/leaves?')).at(-1).endpoint;
    assert.match(last, /status=Overdue/);
    assert.match(last, /unlistedOnly=true/);
    assert.match(last, /page=1(&|$)/);
});

test('une sortie dont le retour est dépassé peut être enregistrée ; une sortie rentrée ne le peut plus', async () => {
    const { logic } = boot();
    assert.equal(logic.canRecordReturn(LEAVE('a', { status: 'Overdue' })), true);
    assert.equal(logic.canRecordReturn(LEAVE('a', { status: 'Active' })), true);
    assert.equal(logic.canRecordReturn(LEAVE('a', { status: 'Pending' })), true);
    assert.equal(logic.canRecordReturn(LEAVE('a', { status: 'Returned', actualReturnDate: TODAY })), false);
});

test('erreur de chargement : message visible, pas de registre fantôme', async () => {
    const { tab } = boot({ fail: (m, e) => (e.startsWith('/boarding/leaves?') ? apiError('MODULE_DISABLED', 403) : undefined) });
    const t = await ready(tab());
    assert.match(t.error, /Internat/);
    assert.deepEqual(plain(t.items), []);
});

test('échec du chargement des pavillons du filtre : signalé, le registre reste utilisable', async () => {
    const { tab } = boot({ fail: (m, e) => (e === '/boarding/dormitories' ? Object.assign(new Error(''), { status: 500 }) : undefined) });
    const t = await ready(tab());
    assert.match(t.filterError, /pavillons/i);
    assert.equal(t.items.length, 2);
});

// ---------------------------------------------------------------- composant : déclaration

async function openedDeclare(options) {
    const ctx = boot(options);
    const t = await ready(ctx.tab());
    t.openDeclare();
    return { ...ctx, t };
}

test('la modale de déclaration démarre avec les dates du jour et le motif Week-end', async () => {
    const { t } = await openedDeclare();
    assert.equal(t.declare.open, true);
    assert.equal(t.declare.form.leaveDate, TODAY);
    assert.equal(t.declare.form.expectedReturnDate, TODAY);
    assert.equal(t.declare.form.reason, 'Weekend');
});

test('recherche du pensionnaire : 2 caractères au moins, seulement les séjours actifs', async () => {
    const { t, calls } = await openedDeclare();

    t.declare.search = 'A';
    await t.searchBoarders();
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/boarding/boarders?')).length, 0);

    t.declare.search = 'Awa';
    await t.searchBoarders();
    const call = calls.find((c) => c.endpoint.startsWith('/boarding/boarders?'));
    assert.match(call.endpoint, /status=active/);
    assert.match(call.endpoint, /search=Awa/);
    assert.equal(t.declare.results.length, 1);
});

test('choisir un pensionnaire charge ses personnes habilitées (GET fiche) pour comparer l\'accompagnateur', async () => {
    const { t, calls } = await openedDeclare();
    await t.selectBoarder(BOARDER);

    assert.ok(calls.some((c) => c.endpoint === '/boarding/boarders/s1'));
    assert.equal(t.declare.boarder.id, 's1');
    assert.deepEqual(plain(t.declare.persons.map((p) => p.name)), ['Fatou Diop']);
});

test('accompagnateur hors liste : AVERTISSEMENT visible, déclaration toujours possible (Q5)', async () => {
    const { t, calls } = await openedDeclare();
    await t.selectBoarder(BOARDER);

    t.declare.form.accompaniedBy = 'Moussa Ba';
    assert.equal(t.companionUnlisted, true, 'avertissement affiché');
    assert.equal(t.canSubmitDeclare, true, 'mais jamais bloqué');

    await t.submitDeclare();
    const post = calls.find((c) => c.method === 'POST');
    assert.ok(post, 'la déclaration part malgré l\'avertissement');
    assert.equal(post.endpoint, '/boarding/leaves');
    assert.equal(post.body.accompaniedBy, 'Moussa Ba');
    assert.equal('isCompanionUnlisted' in post.body, false);
    assert.equal(t.declare.open, false);
    assert.match(t.notice, /enregistrée/);
});

test('accompagnateur habilité (casse et espaces ignorés) : pas d\'avertissement', async () => {
    const { t } = await openedDeclare();
    await t.selectBoarder(BOARDER);
    t.declare.form.accompaniedBy = '  fatou   DIOP ';
    assert.equal(t.companionUnlisted, false);
});

test("pas d'avertissement tant que l'accompagnateur n'est pas saisi ni le pensionnaire choisi", async () => {
    const { t } = await openedDeclare();
    assert.equal(t.companionUnlisted, false);
    await t.selectBoarder(BOARDER);
    assert.equal(t.companionUnlisted, false, 'champ vide : pas de faux avertissement');
});

test('LEAVE_ALREADY_OPEN : message clair, la modale reste ouverte avec la saisie', async () => {
    const { t } = await openedDeclare({ fail: (m, e) => (m === 'POST' ? apiError('LEAVE_ALREADY_OPEN') : undefined) });
    await t.selectBoarder(BOARDER);
    t.declare.form.accompaniedBy = 'Fatou Diop';
    await t.submitDeclare();

    assert.match(t.declare.error, /sortie en cours/i);
    assert.equal(t.declare.open, true);
    assert.equal(t.declare.form.accompaniedBy, 'Fatou Diop');
    assert.equal(t.declare.saving, false);
});

test("un formulaire invalide n'envoie rien", async () => {
    const { t, calls } = await openedDeclare();
    await t.selectBoarder(BOARDER);
    t.declare.form.accompaniedBy = '';
    await t.submitDeclare();

    assert.equal(calls.some((c) => c.method === 'POST'), false);
    assert.ok(t.declare.errors.accompaniedBy);
});

test('changer le texte de recherche après avoir choisi un pensionnaire invalide la sélection', async () => {
    const { t } = await openedDeclare();
    await t.selectBoarder(BOARDER);
    t.declare.search = 'Autre nom';
    t.onBoarderSearchInput();

    assert.equal(t.declare.boarder, null);
    assert.deepEqual(plain(t.declare.persons), []);
    assert.equal(t.canSubmitDeclare, false);
});

// ---------------------------------------------------------------- composant : retour

test('le retour propose la date du jour et envoie le rowVersion de la sortie', async () => {
    const { tab, calls } = boot();
    const t = await ready(tab());
    t.openReturn(t.items[0]);

    assert.equal(t.returnModal.date, TODAY);
    await t.submitReturn();

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/boarding/leaves/l1/return');
    assert.deepEqual(put.body, { rowVersion: 21, actualReturnDate: TODAY });
    assert.equal(t.returnModal.open, false);
    assert.match(t.notice, /Retour de Awa Diop enregistré/);
});

test('LEAVE_ALREADY_RETURNED : message clair et registre rechargé', async () => {
    const { tab, calls } = boot({ fail: (m) => (m === 'PUT' ? apiError('LEAVE_ALREADY_RETURNED') : undefined) });
    const t = await ready(tab());
    const before = calls.filter((c) => c.endpoint.startsWith('/boarding/leaves?')).length;
    t.openReturn(t.items[0]);
    await t.submitReturn();

    assert.match(t.returnModal.error, /déjà/i);
    assert.equal(t.returnModal.stale, true);
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/boarding/leaves?')).length, before + 1, 'registre relu');
});

test('CONCURRENCY_CONFLICT sur le retour : registre rechargé, confirmation bloquée (jeton périmé)', async () => {
    const { tab, calls } = boot({ fail: (m) => (m === 'PUT' ? apiError('CONCURRENCY_CONFLICT') : undefined) });
    const t = await ready(tab());
    const before = calls.filter((c) => c.endpoint.startsWith('/boarding/leaves?')).length;
    t.openReturn(t.items[0]);
    await t.submitReturn();

    assert.match(t.returnModal.error, /modifié par une autre personne/);
    assert.equal(t.returnModal.stale, true);
    assert.equal(calls.filter((c) => c.endpoint.startsWith('/boarding/leaves?')).length, before + 1);
});

test('une date de retour vide est refusée avant l\'appel', async () => {
    const { tab, calls } = boot();
    const t = await ready(tab());
    t.openReturn(t.items[0]);
    t.returnModal.date = '';
    await t.submitReturn();

    assert.equal(calls.some((c) => c.method === 'PUT'), false);
    assert.ok(t.returnModal.error);
});
