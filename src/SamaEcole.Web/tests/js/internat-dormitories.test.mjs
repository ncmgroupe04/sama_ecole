/**
 * Onglet « Pavillons » de /internat (lot G, tâche 5) — composant `dormitoriesTab` (wwwroot/js/internat-dormitories.js).
 *
 * Après le lot B, plus aucune autre IHM ne permet de créer un pavillon : cet onglet est le seul accès. Ces tests prouvent que l'écran appelle
 * les bonnes routes de /api/v1/boarding avec les bons corps, n'écrit que pour les rôles autorisés, recharge sur conflit de version au lieu d'écraser,
 * et traduit les refus métier (non vide, doublon, doublon supprimé) sans refaire les règles du serveur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const DORMITORIES = [
    { id: 'd1', name: 'Pavillon Oustaz Ahmad', gender: 'Garcons', supervisorName: 'M. Ndiaye', supervisorPhone: '77 000 11 22', supervisorUserId: null,
      roomCount: 2, capacity: 8, occupiedBeds: 3, maintenanceBeds: 1, occupancyRate: 37.5, rowVersion: 11 },
    { id: 'd2', name: 'Pavillon Fatima', gender: 'Filles', supervisorName: null, supervisorPhone: null, supervisorUserId: null,
      roomCount: 0, capacity: 0, occupiedBeds: 0, maintenanceBeds: 0, occupancyRate: 0, rowVersion: 12 }
];

const DETAIL = {
    id: 'd1', name: 'Pavillon Oustaz Ahmad', gender: 'Garcons', supervisorName: 'M. Ndiaye', supervisorPhone: '77 000 11 22', supervisorUserId: null,
    notes: null, capacity: 4, occupiedBeds: 1, rowVersion: 11,
    rooms: [{
        id: 'r1', dormitoryId: 'd1', name: 'Chambre 101', capacity: 3, rowVersion: 21,
        beds: [
            { id: 'b1', dormitoryRoomId: 'r1', bedNumber: 1, status: 'Occupied', occupantBoarderId: 'x', occupantName: 'Awa Diop', rowVersion: 31 },
            { id: 'b2', dormitoryRoomId: 'r1', bedNumber: 2, status: 'Available', occupantBoarderId: null, occupantName: null, rowVersion: 32 },
            { id: 'b3', dormitoryRoomId: 'r1', bedNumber: 3, status: 'Maintenance', occupantBoarderId: null, occupantName: null, rowVersion: 33 }
        ]
    }]
};

function apiError(code, status = 409, message = 'technical') {
    const error = new Error(message);
    error.code = code;
    error.status = status;
    return error;
}

/**
 * @param {object} options
 * @param {string} options.role       rôle de l'utilisateur connecté
 * @param {Function} options.fail     (method, endpoint) → Error | undefined : fait échouer un appel
 */
function boot({ role = 'Directeur', fail = () => undefined } = {}) {
    const calls = [];
    const trash = { notified: [], opened: [] };

    const record = async (method, endpoint, body, result) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        const error = fail(method, endpoint);
        if (error) throw error;
        return result;
    };

    const ctx = loadScripts(['internat-helpers.js', 'internat-dormitories.js'], {
        preload: {
            auth: { role },
            softDeleteTrash: { notifyChanged: (k) => trash.notified.push(k), openFor: (k, n) => trash.opened.push([k, n]) },
            api: {
                get: (endpoint) => record('GET', endpoint, undefined, endpoint === '/boarding/dormitories' ? DORMITORIES : DETAIL),
                post: (endpoint, body) => record('POST', endpoint, body, {}),
                put: (endpoint, body) => record('PUT', endpoint, body, {}),
                delete: (endpoint) => record('DELETE', endpoint, undefined, null),
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : (err && err.message) || fallback)
            }
        }
    });

    return { ctx, calls, trash, tab: () => ctx.component('dormitoriesTab') };
}

async function ready(tab) {
    await flush();
    return tab;
}

// ---------------------------------------------------------------- lecture et droits

test('au chargement, la liste vient de GET /boarding/dormitories', async () => {
    const t = boot();
    const tab = await ready(t.tab());

    assert.deepEqual(plain(t.calls[0]), { method: 'GET', endpoint: '/boarding/dormitories' });
    assert.equal(tab.dormitories.length, 2);
    assert.equal(tab.loading, false);
});

test('seuls le Directeur et le Secrétariat peuvent écrire ; le Surveillant lit seulement', async () => {
    for (const [role, canManage] of [['Directeur', true], ['Secretariat', true], ['Surveillant', false]]) {
        const tab = await ready(boot({ role }).tab());

        assert.equal(tab.canManage, canManage, role);
    }
});

test('« Mixte » n\'est jamais proposé dans le formulaire', async () => {
    const tab = await ready(boot().tab());

    assert.deepEqual(plain(tab.genderChoices.map((g) => g.value)), ['Garcons', 'Filles']);
});

test('une erreur de chargement s\'affiche sans casser l\'écran', async () => {
    const t = boot({ fail: (m, e) => (e === '/boarding/dormitories' ? apiError('MODULE_DISABLED', 403) : undefined) });
    const tab = await ready(t.tab());

    assert.match(tab.error, /module/i);
    assert.equal(tab.loading, false);
});

test('le détail d\'un pavillon vient de GET /boarding/dormitories/{id}', async () => {
    const t = boot();
    const tab = await ready(t.tab());

    await tab.openDetail(DORMITORIES[0]);

    assert.equal(t.calls.at(-1).endpoint, '/boarding/dormitories/d1');
    assert.equal(tab.detail.rooms[0].beds.length, 3);
});

// ---------------------------------------------------------------- pavillon : création / modification

test('création : validation avant l\'appel (nom et genre obligatoires, rien n\'est envoyé)', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    const before = t.calls.length;

    tab.openCreate();
    await tab.saveDormitory();

    assert.equal(t.calls.length, before, 'aucun appel réseau tant que le formulaire est invalide');
    assert.ok(tab.form.errors.name && tab.form.errors.gender);
    assert.equal(tab.form.open, true);
});

test('création : POST /boarding/dormitories avec le corps nettoyé, puis rechargement et fermeture', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openCreate();
    Object.assign(tab.form.values, { name: '  Pavillon Nord ', gender: 'Filles', supervisorName: ' Mme Sow ', supervisorPhone: '', notes: '' });

    await tab.saveDormitory();

    const post = t.calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/boarding/dormitories');
    assert.deepEqual(post.body, { name: 'Pavillon Nord', gender: 'Filles', supervisorName: 'Mme Sow', supervisorPhone: null, supervisorUserId: null, notes: null });
    assert.equal(tab.form.open, false);
    assert.equal(t.calls.filter((c) => c.method === 'GET' && c.endpoint === '/boarding/dormitories').length, 2, 'la liste est relue');
    assert.match(tab.notice, /créé/i);
});

test('modification : PUT avec le rowVersion lu, jamais un jeton inventé', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openEdit(DORMITORIES[0]);
    tab.form.values.name = 'Pavillon Oustaz Ahmad (rénové)';

    await tab.saveDormitory();

    const put = t.calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/boarding/dormitories/d1');
    assert.equal(put.body.rowVersion, 11);
    assert.equal(put.body.name, 'Pavillon Oustaz Ahmad (rénové)');
});

test('modification d\'un pavillon repris en « Mixte » : le genre est à choisir, il n\'est pas conservé', async () => {
    const tab = await ready(boot().tab());

    tab.openEdit({ ...DORMITORIES[0], gender: 'Mixte' });

    assert.equal(tab.form.values.gender, '');
    await tab.saveDormitory();
    assert.ok(tab.form.errors.gender, 'le Directeur doit qualifier le pavillon');
});

test('conflit de version (409) : message clair et rechargement, la saisie d\'un autre n\'est pas écrasée', async () => {
    const t = boot({ fail: (m) => (m === 'PUT' ? apiError('CONCURRENCY_CONFLICT') : undefined) });
    const tab = await ready(t.tab());
    tab.openEdit(DORMITORIES[0]);
    const loadsBefore = t.calls.filter((c) => c.method === 'GET').length;

    await tab.saveDormitory();

    assert.match(tab.form.error, /modifié par une autre personne/i);
    assert.ok(t.calls.filter((c) => c.method === 'GET').length > loadsBefore, 'la liste est relue');
    assert.equal(tab.form.open, true, 'le formulaire reste ouvert pour relire puis recommencer');
});

test('doublon : un nom déjà actif est signalé', async () => {
    const tab = await ready(boot({ fail: (m) => (m === 'POST' ? apiError('ACTIVE_ENTITY_CONFLICT') : undefined) }).tab());
    tab.openCreate();
    Object.assign(tab.form.values, { name: 'Pavillon Fatima', gender: 'Filles' });

    await tab.saveDormitory();

    assert.match(tab.form.error, /porte déjà ce nom/i);
    assert.equal(tab.form.archivedConflict, false);
});

test('doublon SUPPRIMÉ : on propose la corbeille plutôt que de dupliquer', async () => {
    const t = boot({ fail: (m) => (m === 'POST' ? apiError('ARCHIVED_ENTITY_EXISTS') : undefined) });
    const tab = await ready(t.tab());
    tab.openCreate();
    Object.assign(tab.form.values, { name: 'Pavillon Ancien', gender: 'Garcons' });

    await tab.saveDormitory();
    assert.equal(tab.form.archivedConflict, true);
    assert.match(tab.form.error, /restaurez-le/i);

    tab.showArchivedDuplicate('dormitories', 'Pavillon Ancien');
    assert.deepEqual(plain(t.trash.opened), [['dormitories', 'Pavillon Ancien']]);
    assert.equal(tab.form.open, false);
});

// ---------------------------------------------------------------- chambres et lits

test('chambre : 1 à 40 lits, sinon aucun appel', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openAddRoom(DORMITORIES[0]);
    Object.assign(tab.roomForm.values, { name: 'Chambre 9', bedCount: 41 });
    const before = t.calls.length;

    await tab.saveRoom();

    assert.equal(t.calls.length, before);
    assert.ok(tab.roomForm.errors.bedCount);
});

test('chambre : POST /boarding/rooms avec le pavillon, le nom et le nombre de lits', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openAddRoom(DORMITORIES[0]);
    Object.assign(tab.roomForm.values, { name: ' Chambre 9 ', bedCount: '6' });

    await tab.saveRoom();

    const post = t.calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/boarding/rooms');
    assert.deepEqual(post.body, { dormitoryId: 'd1', name: 'Chambre 9', bedCount: 6 });
    assert.equal(tab.roomForm.open, false);
});

test('ajout d\'un lit : POST /boarding/beds sans numéro (le serveur prend le suivant)', async () => {
    const t = boot();
    const tab = await ready(t.tab());

    await tab.addBed({ id: 'r1' });

    const post = t.calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/boarding/beds');
    assert.deepEqual(post.body, { dormitoryRoomId: 'r1', bedNumber: null });
});

test('maintenance : un lit disponible passe en maintenance avec son rowVersion, et inversement', async () => {
    const t = boot();
    const tab = await ready(t.tab());

    await tab.toggleMaintenance(DETAIL.rooms[0].beds[1]);   // Available → Maintenance
    await tab.toggleMaintenance(DETAIL.rooms[0].beds[2]);   // Maintenance → Available

    const puts = t.calls.filter((c) => c.method === 'PUT');
    assert.deepEqual(puts.map((p) => [p.endpoint, p.body]), [
        ['/boarding/beds/b2/status', { status: 'Maintenance', rowVersion: 32 }],
        ['/boarding/beds/b3/status', { status: 'Available', rowVersion: 33 }]
    ]);
});

test('maintenance : un lit OCCUPÉ ne se bascule jamais depuis cet onglet (il faut d\'abord libérer le pensionnaire)', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    const before = t.calls.length;

    await tab.toggleMaintenance(DETAIL.rooms[0].beds[0]);

    assert.equal(tab.canToggleMaintenance(DETAIL.rooms[0].beds[0]), false);
    assert.equal(t.calls.length, before);
});

test('maintenance : le Surveillant (lecture seule) ne peut pas basculer un lit', async () => {
    const t = boot({ role: 'Surveillant' });
    const tab = await ready(t.tab());
    const before = t.calls.length;

    await tab.toggleMaintenance(DETAIL.rooms[0].beds[1]);

    assert.equal(t.calls.length, before);
});

// ---------------------------------------------------------------- suppression logique

test('suppression : DELETE avec le rowVersion, corbeille notifiée, rechargement', async () => {
    const t = boot();
    const tab = await ready(t.tab());
    tab.openRemoval('dormitory', DORMITORIES[1]);

    await tab.confirmRemoval();

    const del = t.calls.find((c) => c.method === 'DELETE');
    assert.equal(del.endpoint, '/boarding/dormitories/d2?rowVersion=12');
    assert.deepEqual(plain(t.trash.notified), ['dormitories']);
    assert.equal(tab.removal.open, false);
});

test('suppression : chaque type vise sa route et sa corbeille', async () => {
    const t = boot();
    const tab = await ready(t.tab());

    tab.openRemoval('room', DETAIL.rooms[0]);
    await tab.confirmRemoval();
    tab.openRemoval('bed', DETAIL.rooms[0].beds[1]);
    await tab.confirmRemoval();

    assert.deepEqual(t.calls.filter((c) => c.method === 'DELETE').map((c) => c.endpoint),
        ['/boarding/rooms/r1?rowVersion=21', '/boarding/beds/b2?rowVersion=32']);
    assert.deepEqual(plain(t.trash.notified), ['dormitory-rooms', 'beds']);
});

test('suppression d\'un pavillon non vide : RESOURCE_IN_USE expliqué, rien n\'est retiré de l\'écran', async () => {
    const t = boot({ fail: (m) => (m === 'DELETE' ? apiError('RESOURCE_IN_USE') : undefined) });
    const tab = await ready(t.tab());
    tab.openRemoval('dormitory', DORMITORIES[0]);

    await tab.confirmRemoval();

    assert.match(tab.removal.error, /contient encore/i);
    assert.equal(tab.removal.open, true, 'la modale reste ouverte avec l\'explication');
    assert.deepEqual(plain(t.trash.notified), [], 'rien n\'a été supprimé : la corbeille n\'est pas notifiée');
    assert.equal(tab.dormitories.length, 2);
});

test('libellé de confirmation : un lit se désigne par son numéro, le reste par son nom', async () => {
    const tab = await ready(boot().tab());

    tab.openRemoval('bed', DETAIL.rooms[0].beds[1]);
    assert.equal(tab.removalLabel(), 'le lit n° 2');
    tab.openRemoval('room', DETAIL.rooms[0]);
    assert.equal(tab.removalLabel(), '« Chambre 101 »');
});
