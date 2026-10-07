/**
 * Onglet « Occupation » de /internat (lot G, tâche 7) — logique pure `window.occupationLogic` et composant `occupationTab`
 * (wwwroot/js/internat-occupation.js).
 *
 * Enjeu : cet écran était le dernier consommateur des routes héritées GET /internat/dashboard, GET /internat/students/search et
 * POST /internat/assignments/{id}. Ces tests prouvent qu'il n'appelle plus que /api/v1/boarding, que le jeton envoyé pour un transfert est celui du SÉJOUR
 * lu dans la recherche, et que les refus du serveur (lit pris, conflit de version) rechargent l'écran au lieu de le laisser sur des données périmées.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { loadScripts, flush, plain } from './harness.mjs';

const JS_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'wwwroot', 'js');

const SUMMARIES = [{ id: 'd1', name: 'Pavillon Oustaz Ahmad' }, { id: 'd2', name: 'Pavillon Fatima' }];

const bed = (id, number, status, occupant = null) => ({
    id, dormitoryRoomId: 'r', bedNumber: number, status,
    occupantBoarderId: occupant ? occupant.id : null, occupantName: occupant ? occupant.name : null, rowVersion: 1
});

const DETAILS = {
    d1: {
        id: 'd1', name: 'Pavillon Oustaz Ahmad', rooms: [
            { id: 'r1', name: 'Chambre 101', capacity: 2, rowVersion: 1, beds: [
                bed('b1', 1, 'Occupied', { id: 's1', name: 'Awa Diop' }), bed('b2', 2, 'Available'), bed('b3', 3, 'Maintenance')] },
            { id: 'r2', name: 'Chambre 102', capacity: 2, rowVersion: 1, beds: [
                bed('b4', 1, 'Occupied', { id: 's2', name: 'Binta Fall' }), bed('b5', 2, 'Occupied', { id: 's3', name: 'Cheikh Ba' })] }
        ]
    },
    d2: { id: 'd2', name: 'Pavillon Fatima', rooms: [{ id: 'r3', name: 'Chambre 201', capacity: 2, rowVersion: 1, beds: [bed('b6', 1, 'Available'), bed('b7', 2, 'Available')] }] }
};

const COUNTS = { Interne: 3, DemiPensionnaire: 1, awaitingBed: 1 };

/** Candidats de la recherche : un externe, un interne logé en b1, un demi-pensionnaire. */
const EXTERNAL = { studentId: 'st1', enrollmentId: 'e1', matricule: 'M-1', fullName: 'Moussa Sow', classroomName: 'CM2', boarderId: null, regime: null, bedId: null, bedNumber: null, roomName: null, dormitoryName: null, rowVersion: null };
const LODGED = { studentId: 'st2', enrollmentId: 'e2', matricule: 'M-2', fullName: 'Awa Diop', classroomName: 'CM2', boarderId: 's1', regime: 'Interne', bedId: 'b1', bedNumber: 1, roomName: 'Chambre 101', dormitoryName: 'Pavillon Oustaz Ahmad', rowVersion: 77 };
const HALF = { studentId: 'st3', enrollmentId: 'e3', matricule: 'M-3', fullName: 'Fatou Gaye', classroomName: 'CM1', boarderId: 's9', regime: 'DemiPensionnaire', bedId: null, bedNumber: null, roomName: null, dormitoryName: null, rowVersion: 5 };

function apiError(code, status = 409) {
    const error = new Error('technical');
    error.code = code;
    error.status = status;
    return error;
}

function boot({ role = 'Directeur', fail = () => undefined, candidates = [EXTERNAL, LODGED, HALF] } = {}) {
    const calls = [];
    const record = async (method, endpoint, body, result) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        const error = fail(method, endpoint);
        if (error) throw error;
        return result;
    };
    const route = (endpoint) => {
        if (endpoint === '/boarding/dormitories') return SUMMARIES;
        const detail = endpoint.match(/^\/boarding\/dormitories\/(d\d)$/);
        if (detail) return DETAILS[detail[1]];
        if (endpoint.includes('regime=Interne')) return { totalCount: COUNTS.Interne, items: [] };
        if (endpoint.includes('regime=DemiPensionnaire')) return { totalCount: COUNTS.DemiPensionnaire, items: [] };
        if (endpoint.includes('status=awaitingBed')) return { totalCount: COUNTS.awaitingBed, items: [] };
        if (endpoint.startsWith('/boarding/boardable-students')) return candidates;
        return null;
    };

    const ctx = loadScripts(['internat-helpers.js', 'internat-occupation.js'], {
        preload: {
            auth: { role },
            api: {
                get: (endpoint) => record('GET', endpoint, undefined, route(endpoint)),
                post: (endpoint, body) => record('POST', endpoint, body, {}),
                delete: (endpoint) => record('DELETE', endpoint, undefined, null),
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : (err && err.message) || fallback)
            }
        }
    });

    return { ctx, calls, tab: () => ctx.component('occupationTab'), logic: ctx.window.occupationLogic };
}

async function ready(tab) {
    await flush();
    return tab;
}

// ---------------------------------------------------------------- logique pure

test('freeBeds : seuls les lits « Disponible » sont proposés (ni occupé, ni en maintenance)', () => {
    const { logic } = boot();

    assert.deepEqual(plain(logic.freeBeds(DETAILS.d1.rooms[0]).map((b) => b.id)), ['b2']);
    assert.deepEqual(plain(logic.freeBeds(DETAILS.d1.rooms[1])), []);
});

test('isRoomFull : une chambre sans lit utilisable n\'est pas « complète »', () => {
    const { logic } = boot();

    assert.equal(logic.isRoomFull(DETAILS.d1.rooms[1]), true);
    assert.equal(logic.isRoomFull(DETAILS.d1.rooms[0]), false);
    assert.equal(logic.isRoomFull({ capacity: 0, beds: [] }), false);
});

test('flattenRooms : chambres de tous les pavillons, occupants tirés des lits occupés', () => {
    const { logic } = boot();

    const rooms = plain(logic.flattenRooms([DETAILS.d1, DETAILS.d2]));

    assert.deepEqual(rooms.map((r) => r.roomName), ['Chambre 101', 'Chambre 102', 'Chambre 201']);
    assert.deepEqual(rooms[0].occupants, [{ boarderId: 's1', fullName: 'Awa Diop', bedId: 'b1', bedNumber: 1 }]);
    assert.deepEqual([rooms[0].occupied, rooms[0].capacity, rooms[0].full], [1, 2, false]);
    assert.deepEqual([rooms[1].occupied, rooms[1].full], [2, true]);
});

test('bedOptions : lits libres de la chambre ciblée, ou de tout l\'internat sans chambre', () => {
    const { logic } = boot();
    const rooms = logic.flattenRooms([DETAILS.d1, DETAILS.d2]);

    assert.deepEqual(plain(logic.bedOptions(rooms, 'r1')), [{ value: 'b2', label: 'Pavillon Oustaz Ahmad — Chambre 101 — Lit 2' }]);
    assert.deepEqual(plain(logic.bedOptions(rooms, null).map((o) => o.value)), ['b2', 'b6', 'b7']);
    assert.deepEqual(plain(logic.bedOptions(rooms, 'r2')), [], 'chambre complète : aucun lit à proposer');
});

test('situationLabel et defaultRegime : un externe devient Interne par défaut, un demi-pensionnaire le reste', () => {
    const { logic } = boot();

    assert.equal(logic.situationLabel(EXTERNAL), 'Externe');
    assert.match(logic.situationLabel(LODGED), /Interne — Pavillon Oustaz Ahmad · Chambre 101 · Lit 1/);
    assert.equal(logic.situationLabel(HALF), 'Demi-pensionnaire');
    assert.equal(logic.defaultRegime(EXTERNAL), 'Interne');
    assert.equal(logic.defaultRegime(LODGED), 'Interne');
    assert.equal(logic.defaultRegime(HALF), 'DemiPensionnaire', 'jamais de conversion silencieuse du régime');
});

test('isTransfer : seulement pour un élève déjà logé que l\'on déplace vers un AUTRE lit', () => {
    const { logic } = boot();

    assert.equal(logic.isTransfer(EXTERNAL, 'Interne', 'b2'), false);
    assert.equal(logic.isTransfer(LODGED, 'Interne', 'b2'), true);
    assert.equal(logic.isTransfer(LODGED, 'Interne', 'b1'), false, 'même lit : rien à transférer');
    assert.equal(logic.isTransfer(LODGED, 'Externe', ''), false, 'libérer n\'est pas un transfert');
    assert.equal(logic.isTransfer(null, 'Interne', 'b2'), false);
});

test('buildAssignBody : lit pour un interne seulement, rowVersion du séjour seulement s\'il y a un séjour', () => {
    const { logic } = boot();

    assert.deepEqual(plain(logic.buildAssignBody(EXTERNAL, 'Interne', 'b2', true)),
        { enrollmentId: 'e1', regime: 'Interne', bedId: 'b2', includeBoardingFee: true, rowVersion: null });
    assert.deepEqual(plain(logic.buildAssignBody(LODGED, 'Interne', 'b6', false)),
        { enrollmentId: 'e2', regime: 'Interne', bedId: 'b6', includeBoardingFee: false, rowVersion: 77 });
    assert.deepEqual(plain(logic.buildAssignBody(EXTERNAL, 'DemiPensionnaire', 'b2', true)),
        { enrollmentId: 'e1', regime: 'DemiPensionnaire', bedId: null, includeBoardingFee: true, rowVersion: null },
        'un demi-pensionnaire n\'a jamais de lit, même si un lit est encore sélectionné');
    assert.equal(logic.buildAssignBody(HALF, 'Interne', 'b2', false).rowVersion, 5, 'un demi-pensionnaire promu interne = transfert, avec son jeton');
});

test('assignBlocker : messages avant l\'appel', () => {
    const { logic } = boot();

    assert.match(logic.assignBlocker(null, 'Interne', ''), /élève/i);
    assert.match(logic.assignBlocker(EXTERNAL, 'Interne', ''), /lit/i);
    assert.equal(logic.assignBlocker(EXTERNAL, 'Interne', 'b2'), '');
    assert.equal(logic.assignBlocker(EXTERNAL, 'DemiPensionnaire', ''), '');
    assert.match(logic.assignBlocker(EXTERNAL, 'Externe', ''), /pas de séjour/i, 'on ne libère pas un élève sans séjour');
    assert.equal(logic.assignBlocker(LODGED, 'Externe', ''), '');
});

// ---------------------------------------------------------------- lecture

test('au chargement : pavillons, détail de chacun et trois compteurs — uniquement sur /boarding', async () => {
    const t = boot();
    const tab = await ready(t.tab());

    assert.deepEqual(t.calls.map((c) => `${c.method} ${c.endpoint}`).sort(), [
        'GET /boarding/boarders?regime=DemiPensionnaire&pageSize=1',
        'GET /boarding/boarders?regime=Interne&pageSize=1',
        'GET /boarding/boarders?status=awaitingBed&pageSize=1',
        'GET /boarding/dormitories',
        'GET /boarding/dormitories/d1',
        'GET /boarding/dormitories/d2'
    ]);
    assert.equal(tab.loading, false);
    assert.equal(tab.error, '');
});

test('indicateurs : lits occupés / capacité, régimes lus dans totalCount, chambres complètes', async () => {
    const tab = await ready(boot().tab());

    assert.deepEqual(plain(tab.totals), {
        capacity: 6, occupied: 3, roomCount: 3, fullRooms: 1, interne: 3, demiPensionnaire: 1, awaitingBed: 1
    });
});

test('une erreur de chargement s\'affiche sans casser l\'écran', async () => {
    const t = boot({ fail: (m, e) => (e === '/boarding/dormitories' ? apiError('MODULE_DISABLED', 403) : undefined) });
    const tab = await ready(t.tab());

    assert.match(tab.error, /module/i);
    assert.equal(tab.loading, false);
});

// ---------------------------------------------------------------- recherche

test('la recherche appelle GET /boarding/boardable-students avec le terme encodé, 2 caractères au moins', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    const before = t.calls.length;

    tab.openAssignModal(null);
    tab.assignModal.searchTerm = 'a';
    await tab.searchStudents();
    assert.equal(t.calls.length, before, 'un seul caractère : aucun appel');

    tab.assignModal.searchTerm = 'Awa Dio';
    await tab.searchStudents();
    assert.equal(t.calls.at(-1).endpoint, '/boarding/boardable-students?term=Awa%20Dio');
    assert.equal(tab.assignModal.results.length, 3);
});

test('une sélection périmée est invalidée quand l\'utilisateur modifie sa recherche', async () => {
    const tab = await ready(boot().tab());
    tab.openAssignModal(null);
    tab.selectStudent(EXTERNAL);

    tab.assignModal.searchTerm = 'Autre nom';
    tab.onSearchInput();

    assert.equal(tab.assignModal.selected, null, 'sinon Confirmer soumettrait l\'élève précédent');
    tab.closeAssignModal();
});

test('sélection : le lit libre de la chambre ciblée est pré-sélectionné, le régime suit la situation de l\'élève', async () => {
    const tab = await ready(boot().tab());
    tab.openAssignModal(tab.rooms[0]);

    tab.selectStudent(EXTERNAL);
    assert.deepEqual([tab.assignModal.regime, tab.assignModal.bedId], ['Interne', 'b2']);

    tab.selectStudent(HALF);
    assert.equal(tab.assignModal.regime, 'DemiPensionnaire');
});

test('« Libérer » n\'est proposé qu\'à un élève qui a un séjour', async () => {
    const tab = await ready(boot().tab());
    tab.openAssignModal(null);

    tab.selectStudent(EXTERNAL);
    assert.deepEqual(plain(tab.regimeChoices.map((c) => c.value)), ['Interne', 'DemiPensionnaire']);
    tab.selectStudent(LODGED);
    assert.deepEqual(plain(tab.regimeChoices.map((c) => c.value)), ['Interne', 'DemiPensionnaire', 'Externe']);
});

// ---------------------------------------------------------------- affectation

test('affecter un externe : POST /boarding/assign-bed sans rowVersion, puis rechargement', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openAssignModal(tab.rooms[0]);
    tab.selectStudent(EXTERNAL);
    tab.assignModal.includeBoardingFee = true;

    await tab.confirmAssignment();

    const post = t.calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/boarding/assign-bed');
    assert.deepEqual(post.body, { enrollmentId: 'e1', regime: 'Interne', bedId: 'b2', includeBoardingFee: true, rowVersion: null });
    assert.equal(tab.assignModal.open, false);
    assert.equal(t.calls.filter((c) => c.endpoint === '/boarding/dormitories').length, 2, 'l\'écran est relu');
    assert.match(tab.notice, /Moussa Sow a été affecté/);
});

test('transférer un interne : le rowVersion envoyé est celui du SÉJOUR lu dans la recherche', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openAssignModal(tab.rooms.find((r) => r.roomId === 'r3'));
    tab.selectStudent(LODGED);
    assert.equal(tab.isTransfer(), true);

    await tab.confirmAssignment();

    assert.deepEqual(t.calls.find((c) => c.method === 'POST').body,
        { enrollmentId: 'e2', regime: 'Interne', bedId: 'b6', includeBoardingFee: true, rowVersion: 77 });
});

test('demi-pensionnaire : aucun lit n\'est envoyé et aucun lit n\'est exigé', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openAssignModal(null);
    tab.selectStudent(EXTERNAL);
    tab.assignModal.regime = 'DemiPensionnaire';
    tab.assignModal.bedId = '';

    assert.equal(tab.canConfirm(), true);
    await tab.confirmAssignment();

    assert.equal(t.calls.find((c) => c.method === 'POST').body.bedId, null);
});

test('interne sans lit choisi : aucun appel, message clair', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openAssignModal(tab.rooms.find((r) => r.roomId === 'r2'));   // chambre complète : aucun lit libre
    tab.selectStudent(EXTERNAL);
    const before = t.calls.length;

    assert.equal(tab.canConfirm(), false);
    await tab.confirmAssignment();

    assert.equal(t.calls.length, before);
    assert.match(tab.assignModal.error, /lit/i);
});

test('libérer : DELETE /boarding/unassign-bed/{boarderId}?rowVersion= avec le jeton du séjour', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openChangeOccupantModal(tab.rooms[0], { fullName: 'Awa Diop' });
    await flush();
    tab.selectStudent(LODGED);
    tab.assignModal.regime = 'Externe';

    await tab.confirmAssignment();

    const del = t.calls.find((c) => c.method === 'DELETE');
    assert.equal(del.endpoint, '/boarding/unassign-bed/s1?rowVersion=77');
    assert.equal(t.calls.some((c) => c.method === 'POST'), false);
    assert.match(tab.notice, /libéré/);
});

test('« Changer / Libérer » : la modale s\'ouvre avec la recherche pré-remplie sur l\'occupant', async () => {
    const t = boot();
    const tab = await ready(t.tab());

    tab.openChangeOccupantModal(tab.rooms[0], { fullName: 'Awa Diop' });
    await flush();

    assert.equal(tab.assignModal.searchTerm, 'Awa Diop');
    assert.equal(t.calls.at(-1).endpoint, '/boarding/boardable-students?term=Awa%20Diop');
});

// ---------------------------------------------------------------- refus du serveur

test('lit déjà pris (409 BED_UNAVAILABLE) : message clair, la sélection est abandonnée et l\'écran est relu', async () => {
    const t = boot({ fail: (m) => (m === 'POST' ? apiError('BED_UNAVAILABLE') : undefined) });
    const tab = await ready(t.tab());
    tab.openAssignModal(tab.rooms[0]);
    tab.selectStudent(EXTERNAL);
    const loadsBefore = t.calls.filter((c) => c.endpoint === '/boarding/dormitories').length;

    await tab.confirmAssignment();

    assert.match(tab.assignModal.error, /vient d'être attribué/);
    assert.equal(tab.assignModal.open, true, 'la modale reste ouverte pour choisir un autre lit');
    assert.equal(tab.assignModal.selected, null, 'le jeton et les lits proposés étaient périmés');
    assert.equal(t.calls.filter((c) => c.endpoint === '/boarding/dormitories').length, loadsBefore + 1);
});

test('conflit de version (409) : message clair et rechargement', async () => {
    const t = boot({ fail: (m) => (m === 'POST' ? apiError('CONCURRENCY_CONFLICT') : undefined) });
    const tab = await ready(t.tab());
    tab.openAssignModal(null);
    tab.selectStudent(LODGED);
    tab.assignModal.bedId = 'b6';

    await tab.confirmAssignment();

    assert.match(tab.assignModal.error, /modifié par une autre personne/);
    assert.equal(tab.assignModal.saving, false);
});

test('libération refusée pendant une sortie (409 LEAVE_IN_PROGRESS) : le message du serveur est affiché', async () => {
    const error = Object.assign(new Error('Enregistrez d\'abord le retour de la sortie en cours.'), { code: 'LEAVE_IN_PROGRESS', status: 409 });
    const t = boot({ fail: (m) => (m === 'DELETE' ? error : undefined) });
    const tab = await ready(t.tab());
    tab.openAssignModal(null);
    tab.selectStudent(LODGED);
    tab.assignModal.regime = 'Externe';

    await tab.confirmAssignment();

    assert.match(tab.assignModal.error, /retour de la sortie/);
    assert.equal(tab.assignModal.open, true);
});

// ---------------------------------------------------------------- droits

test('les trois rôles de l\'internat peuvent affecter ; les autres n\'ont pas accès à l\'écran', async () => {
    for (const [role, expected] of [['Directeur', true], ['Secretariat', true], ['Surveillant', true], ['Enseignant', false]]) {
        const tab = await ready(boot({ role }).tab());

        assert.equal(tab.canAssign, expected, role);
    }
});

// ---------------------------------------------------------------- garde-fou de dérive (prérequis du lot F)

test('l\'écran n\'appelle plus AUCUNE des trois routes héritées supprimées au lot F', () => {
    const legacy = [/\/internat\/dashboard/, /\/internat\/students\/search/, /\/internat\/assignments/];
    const strip = (source) => source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');

    for (const file of ['internat.js', 'internat-occupation.js', 'internat-dormitories.js', 'internat-helpers.js']) {
        const source = strip(readFileSync(path.join(JS_DIR, file), 'utf8'));
        for (const pattern of legacy) {
            assert.doesNotMatch(source, pattern, `${file} appelle encore une route héritée (${pattern}) : le lot F ne pourrait pas la supprimer`);
        }
    }
});
