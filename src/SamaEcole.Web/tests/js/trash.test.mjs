/**
 * Corbeille et restauration (wwwroot/js/trash.js + api.js) — conception soft delete 2026-10-01 §3.2.
 *
 * Ce qui compte ici : (1) les routes appelées sont EXACTEMENT celles de l'API (une dérive donnerait une corbeille
 * vide ou un bouton « Restaurer » en 404 sans que rien ne le signale) ; (2) le 409 ARCHIVED_ENTITY_EXISTS est
 * reconnu par les formulaires de création ; (3) la restauration affiche le message du serveur en cas de refus
 * (ACTIVE_ENTITY_CONFLICT, chevauchement, bâtiment supprimé…) au lieu de l'avaler.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

/** Réponse JSON d'une doublure de fetch. */
function reply(status, body) {
    return { ok: status >= 200 && status < 300, status, json: async () => body };
}

function boot(handler) {
    const calls = [];
    const fetchStub = async (url, init = {}) => {
        const call = { url: url.replace('/api/v1', ''), method: init.method || 'GET' };
        calls.push(call);
        return handler(call);
    };

    const ctx = loadScripts(['api.js', 'trash.js'], {
        fetch: fetchStub,
        preload: {
            auth: { accessToken: 'jwt', isAuthenticated: () => false, isAccessTokenStale: () => false, redirectToLogin() {} },
            toast: { success() {}, error() {} }
        }
    });

    const factory = ctx.initAlpine().get('trashPanel');
    return { ctx, calls, panel: (kind) => { const p = factory(kind); p.init(); return p; } };
}

const tick = () => new Promise((resolve) => setImmediate(resolve));

// ------------------------------------------------------------------------------------------------- routes

test("les routes de chaque corbeille correspondent à celles de l'API", () => {
    const { ctx } = boot(async () => reply(200, []));
    const kinds = ctx.window.softDeleteTrash.KINDS;
    const id = '11111111-1111-1111-1111-111111111111';

    const expected = {
        buildings: ['/buildings/deleted', `/buildings/${id}/restore`],
        rooms: ['/rooms/deleted', `/rooms/${id}/restore`],
        classrooms: ['/classrooms/deleted', `/classrooms/${id}/restore`],
        'fee-categories': ['/finance/fee-categories/deleted', `/finance/fee-categories/${id}/restore`],
        mentions: ['/grades/mentions/deleted', `/grades/mentions/${id}/restore`],
        'school-years': ['/school-years/deleted', `/school-years/${id}/restore`]
    };

    assert.deepEqual(Object.keys(kinds).sort(), Object.keys(expected).sort());
    for (const [key, [list, restore]] of Object.entries(expected)) {
        assert.equal(kinds[key].list, list, `${key} : route de la corbeille`);
        assert.equal(kinds[key].restore(id), restore, `${key} : route de restauration`);
    }
});

// ------------------------------------------------------------------------------------------------- 409 à la création

test('un 409 ARCHIVED_ENTITY_EXISTS est reconnu et expose le drapeau archivedConflict', () => {
    const { ctx } = boot(async () => reply(200, []));
    const api = ctx.window.api;

    const archived = Object.assign(new Error('Un bâtiment « Bloc A » existe dans les éléments supprimés : restaurez-le.'),
        { status: 409, code: 'ARCHIVED_ENTITY_EXISTS' });

    assert.equal(api.isArchivedConflict(archived), true);
    const errors = api.toFieldErrors(archived, 'x');
    assert.equal(errors.global, archived.message);
    assert.equal(errors.archivedConflict, true);
});

test("un autre 409 (doublon actif, conflit de version) n'est PAS pris pour un élément supprimé", () => {
    const { ctx } = boot(async () => reply(200, []));
    const api = ctx.window.api;

    const duplicate = Object.assign(new Error('Ce nom existe déjà.'), { status: 409, code: 'DUPLICATE_RECORD' });
    const sameCodeWrongStatus = Object.assign(new Error('x'), { status: 422, code: 'ARCHIVED_ENTITY_EXISTS' });

    assert.equal(api.isArchivedConflict(duplicate), false);
    assert.equal(api.isArchivedConflict(sameCodeWrongStatus), false);
    assert.equal(api.isArchivedConflict(null), false);
    assert.equal(api.toFieldErrors(duplicate, 'x').archivedConflict, undefined);
});

// ------------------------------------------------------------------------------------------------- panneau

test('le panneau charge la corbeille de son type et reste masqué tant qu\'elle est vide', async () => {
    const { calls, panel } = boot(async () => reply(200, []));
    const p = panel('buildings');
    await tick();

    assert.deepEqual(calls.map((c) => `${c.method} ${c.url}`), ['GET /buildings/deleted']);
    assert.equal(p.loaded, true);
    assert.equal(p.count, 0);
    assert.equal(p.visible, false, "rien à restaurer : le panneau n'encombre pas l'écran");
});

test('une corbeille non vide rend le panneau visible et présente chaque ligne', async () => {
    const items = [{ id: 'a', name: 'Bloc A', description: 'Vieux bâtiment', deletedAt: '2026-10-01T10:00:00Z' }];
    const { panel } = boot(async () => reply(200, items));
    const p = panel('buildings');
    await tick();

    assert.equal(p.visible, true);
    assert.equal(p.count, 1);
    assert.equal(p.labelOf(items[0]), 'Bloc A');
    assert.equal(p.detailOf(items[0]), 'Vieux bâtiment');
    assert.match(p.deletedOn(items[0]), /\d{2}\/\d{2}\/\d{4}/);
});

test('restaurer appelle POST .../restore, retire la ligne et prévient les écrans', async () => {
    const items = [{ id: 'm1', label: 'Bien', minAverage: 14 }, { id: 'm2', label: 'Passable', minAverage: 10 }];
    const { ctx, calls, panel } = boot(async (call) => (call.method === 'POST' ? reply(204, null) : reply(200, items)));
    const p = panel('mentions');
    await tick();

    const restored = [];
    ctx.window.addEventListener('trash:restored', (event) => restored.push(event.detail));

    await p.restore(items[0]);

    assert.ok(calls.some((c) => c.method === 'POST' && c.url === '/grades/mentions/m1/restore'));
    assert.deepEqual(p.items.map((i) => i.id), ['m2']);
    assert.match(p.notice, /« Bien » a été restauré/);
    assert.equal(p.error, '');
    assert.equal(p.restoringId, null);
    assert.equal(restored.length, 1);
    assert.equal(restored[0].kind, 'mentions');
    assert.equal(restored[0].id, 'm1');
});

test("un refus 409 de la restauration affiche le message du serveur et garde la ligne", async () => {
    const items = [{ id: 'c1', name: '6e A', level: '6e' }];
    const message = 'Impossible de restaurer : une classe actif(ve) occupe déjà la même identité.';
    const { panel } = boot(async (call) => (call.method === 'POST'
        ? reply(409, { code: 'ACTIVE_ENTITY_CONFLICT', message })
        : reply(200, items)));
    const p = panel('classrooms');
    await tick();

    await p.restore(items[0]);

    assert.equal(p.error, message, 'le serveur explique, l\'écran n\'invente rien');
    assert.equal(p.items.length, 1, 'la ligne reste en corbeille');
    assert.equal(p.notice, '');
});

test('restaurer une année affiche le compte rendu (périodes, affectations laissées supprimées, pas réactivée)', async () => {
    const items = [{ id: 'y1', label: '2024-2025', startDate: '2024-10-01', endDate: '2025-06-30' }];
    const result = { id: 'y1', label: '2024-2025', restoredTerms: 2, restoredAssignments: 1, skippedAssignments: 1 };
    const { panel } = boot(async (call) => (call.method === 'POST' ? reply(200, result) : reply(200, items)));
    const p = panel('school-years');
    await tick();

    await p.restore(items[0]);

    assert.match(p.notice, /2 période\(s\)/);
    assert.match(p.notice, /1 affectation\(s\) d'enseignant/);
    assert.match(p.notice, /1 affectation\(s\) restent supprimées/);
    assert.match(p.notice, /n'est pas réactivée/);
});

test('un rôle sans droit (403) ne voit pas le panneau et aucun message n\'est affiché', async () => {
    const { panel } = boot(async () => reply(403, null));
    const p = panel('fee-categories');
    await tick();

    assert.equal(p.forbidden, true);
    assert.equal(p.visible, false);
    assert.equal(p.error, '');
});

test("depuis un formulaire en conflit, le panneau s'ouvre sur l'élément en doublon", async () => {
    const items = [{ id: 'r1', name: 'Salle 1', deletedAt: '2026-10-01T10:00:00Z' }, { id: 'r2', name: 'Salle 2' }];
    const { ctx, panel } = boot(async () => reply(200, items));
    const p = panel('rooms');
    await tick();
    p.$el = { scrollIntoView() {} };
    assert.equal(p.open, false);

    ctx.window.softDeleteTrash.openFor('rooms', ' salle 1 ');
    await tick();

    assert.equal(p.open, true);
    assert.equal(p.isHighlighted(items[0]), true, 'comparaison insensible à la casse et aux espaces');
    assert.equal(p.isHighlighted(items[1]), false);
});

test("l'ouverture ciblée d'un autre type n'affecte pas ce panneau", async () => {
    const { ctx, panel } = boot(async () => reply(200, []));
    const p = panel('rooms');
    await tick();

    ctx.window.softDeleteTrash.openFor('buildings', 'Bloc A');
    await tick();

    assert.equal(p.open, false);
});

test("une suppression signalée (trash:changed) recharge la corbeille du bon type seulement", async () => {
    const { ctx, calls, panel } = boot(async () => reply(200, []));
    panel('classrooms');
    await tick();
    const before = calls.length;

    ctx.window.softDeleteTrash.notifyChanged('buildings');
    await tick();
    assert.equal(calls.length, before, 'un autre type ne recharge pas');

    ctx.window.softDeleteTrash.notifyChanged('classrooms');
    await tick();
    assert.equal(calls.length, before + 1, 'le même type se recharge');
});
