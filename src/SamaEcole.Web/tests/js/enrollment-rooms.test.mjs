/**
 * Sélecteur de chambre du formulaire d'inscription (enrollments.js) — lot F0.
 *
 * Il lisait l'ancien GET /internat/dashboard, route que le lot F supprime. Il lit désormais GET /boarding/available-rooms, qui ne renvoie que des chambres
 * ayant au moins un lit libre. Le payload de POST /enrollments (boardingStatus + roomId) ne change pas : c'est le serveur qui choisit le lit.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const ROOMS = [
    { roomId: 'r1', roomName: 'Chambre 101', dormitoryId: 'd1', dormitoryName: 'Pavillon Oustaz Ahmad', gender: 'Garcons', freeBeds: 2 },
    { roomId: 'r2', roomName: 'Chambre 201', dormitoryId: 'd2', dormitoryName: 'Pavillon Fatima', gender: 'Filles', freeBeds: 1 }
];

function enrollments({ rooms = ROOMS, fail = false } = {}) {
    const calls = [];
    const ctx = loadScripts(['enrollments.js'], {
        preload: {
            auth: { role: 'Secretariat' },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async (endpoint) => {
                    calls.push(endpoint);
                    if (endpoint === '/boarding/available-rooms') {
                        if (fail) throw Object.assign(new Error('boom'), { status: 500 });
                        return rooms;
                    }
                    return [];
                },
                post: async () => ({}),
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/inscriptions', search: '' }
        }
    });
    const view = ctx.initAlpine().get('enrollmentsView')();
    return { view, calls };
}

test("loadAvailableRooms appelle GET /boarding/available-rooms, jamais l'ancien /internat/dashboard", async () => {
    const { view, calls } = enrollments();
    await view.loadAvailableRooms();

    assert.deepEqual(calls, ['/boarding/available-rooms']);
    assert.equal(calls.some((c) => c.startsWith('/internat')), false);
    assert.deepEqual(plain(view.availableRooms.map((r) => r.roomId)), ['r1', 'r2']);
});

test("la liste du serveur est utilisée telle quelle : plus de filtre « occupants < capacité » côté client", async () => {
    // Le serveur ne renvoie que des chambres avec un lit libre. Une chambre aux champs de l'ancien tableau de bord ne doit pas être filtrée ni masquée.
    const { view } = enrollments({ rooms: [{ roomId: 'r9', roomName: 'Chambre 9', dormitoryName: 'P', freeBeds: 1 }] });
    await view.loadAvailableRooms();
    assert.equal(view.availableRooms.length, 1);
});

test('roomOptions : valeur = identifiant de la chambre, libellé avec pavillon et places libres', async () => {
    const { view } = enrollments();
    await view.loadAvailableRooms();

    assert.deepEqual(plain(view.roomOptions), [
        { value: 'r1', label: 'Chambre 101 — Pavillon Oustaz Ahmad (2 places libres)' },
        { value: 'r2', label: 'Chambre 201 — Pavillon Fatima (1 place libre)' }
    ]);
});

test('roomOptions : vide tant que rien n\'est chargé', () => {
    const { view } = enrollments();
    assert.deepEqual(plain(view.roomOptions), []);
});

test("un échec de chargement laisse le sélecteur vide, sans exception (silence volontaire conservé)", async () => {
    const { view } = enrollments({ fail: true });
    view.availableRooms = [{ roomId: 'ancienne', roomName: 'x', dormitoryName: 'y', freeBeds: 1 }];

    await view.loadAvailableRooms();

    assert.deepEqual(plain(view.availableRooms), []);
});

test("boardingPayload d'un interne : boardingStatus + roomId + pension (le serveur choisit le lit)", async () => {
    const { view } = enrollments();
    await view.loadAvailableRooms();
    view.form.boardingStatus = 'Interne';
    view.form.roomId = 'r1';
    view.form.includeBoardingFee = true;

    assert.deepEqual(plain(view.boardingPayload()), { boardingStatus: 'Interne', roomId: 'r1', includeBoardingFee: true });
});

test("boardingPayload d'un externe : jamais de chambre résiduelle ni de pension", () => {
    const { view } = enrollments();
    view.form.boardingStatus = 'Externe';
    view.form.roomId = 'r1';
    view.form.includeBoardingFee = true;

    assert.deepEqual(plain(view.boardingPayload()), { boardingStatus: 'Externe', roomId: null, includeBoardingFee: false });
});
