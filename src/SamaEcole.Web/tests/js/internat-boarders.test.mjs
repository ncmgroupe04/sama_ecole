/**
 * Onglet « Pensionnaires » de l'écran Internat (wwwroot/js/internat-boarders.js) — lot G4, tâche 8.
 *
 * Enjeux : (1) la fiche médicale d'un mineur n'est affichée ni envoyée pour un rôle qui n'y a pas droit — c'est le serveur qui juge
 * (BoarderDetailReader.CanReadMedical : Directeur et Surveillant), l'écran ne fait que refléter ; (2) `medicalNotes: null` ne s'affiche jamais « null » ;
 * (3) la liste ne se laisse pas écraser par une réponse périmée ; (4) tout passe par /api/v1/boarding.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const ITEM = (id, name, extra = {}) => ({
    id, studentId: `st-${id}`, enrollmentId: `e-${id}`, studentName: name, matricule: `M-${id}`, classroomName: 'CM2',
    regime: 'Interne', isActive: true, bedId: 'b1', bedNumber: 1, roomId: 'r1', roomName: 'Chambre 101',
    dormitoryId: 'd1', dormitoryName: 'Pavillon Oustaz Ahmad', startDate: '2026-09-15', endDate: null, rowVersion: 11, ...extra
});

const DETAIL = (overrides = {}) => ({
    boarder: ITEM('s1', 'Awa Diop'),
    medicalNotes: 'Asthme — ventoline dans le sac.',
    emergencyContactName: 'Fatou Diop',
    emergencyContactPhone: '771234567',
    allowedExitPersons: [{ name: 'Fatou Diop', relationship: 'Mère', phone: '771234567' }],
    leaves: [],
    ...overrides
});

function apiError(code, status = 409, handledGlobally = false) {
    const error = new Error('technical');
    error.code = code;
    error.status = status;
    if (handledGlobally) error.handledGlobally = true;
    return error;
}

function boot({ role = 'Directeur', detail = DETAIL(), list = { items: [ITEM('s1', 'Awa Diop'), ITEM('s2', 'Binta Fall')], totalCount: 2, page: 1, pageSize: 20 }, fail = () => undefined, onGet } = {}) {
    const calls = [];
    const record = async (method, endpoint, body, result) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        const error = fail(method, endpoint);
        if (error) throw error;
        return result;
    };
    const route = (endpoint) => {
        if (onGet) {
            const custom = onGet(endpoint);
            if (custom !== undefined) return custom;
        }
        if (endpoint === '/boarding/dormitories') return [{ id: 'd1', name: 'Pavillon Oustaz Ahmad' }, { id: 'd2', name: 'Pavillon Fatima' }];
        if (/^\/boarding\/dormitories\/d\d$/.test(endpoint)) return { id: 'd1', name: 'Pavillon Oustaz Ahmad', rooms: [{ id: 'r1', name: 'Chambre 101' }, { id: 'r2', name: 'Chambre 102' }] };
        if (endpoint.startsWith('/boarding/boarders?')) return list;
        if (/^\/boarding\/boarders\/[^/?]+$/.test(endpoint)) return detail;
        return null;
    };

    const ctx = loadScripts(['internat-helpers.js', 'internat-boarders.js'], {
        preload: {
            auth: { role },
            pdfPreview: { state: () => ({ showPdfModal: false, openPdfPreview: async () => {} }) },
            api: {
                get: (endpoint) => record('GET', endpoint, undefined, route(endpoint)),
                put: (endpoint, body) => record('PUT', endpoint, body, detail),
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : (err && err.message) || fallback)
            }
        }
    });

    return { ctx, calls, tab: () => ctx.component('boardersTab'), logic: ctx.window.boardersLogic };
}

async function ready(tab) {
    await flush();
    return tab;
}

// ---------------------------------------------------------------- logique pure

test('canSeeMedical : Directeur et Surveillant seulement (miroir de BoarderDetailReader.CanReadMedical)', () => {
    const { logic } = boot();
    assert.equal(logic.canSeeMedical('Directeur'), true);
    assert.equal(logic.canSeeMedical('Surveillant'), true);
    for (const role of ['Secretariat', 'Enseignant', 'Finance', undefined, null, '']) {
        assert.equal(logic.canSeeMedical(role), false, String(role));
    }
});

test('medicalLabel : une fiche vide ou absente se lit « Non renseignée », jamais « null »', () => {
    const { logic } = boot();
    assert.equal(logic.medicalLabel(null), 'Non renseignée');
    assert.equal(logic.medicalLabel(undefined), 'Non renseignée');
    assert.equal(logic.medicalLabel('   '), 'Non renseignée');
    assert.equal(logic.medicalLabel('Asthme'), 'Asthme');
});

test('buildListQuery : omet les filtres vides, encode la recherche, borne la page', () => {
    const { logic } = boot();
    assert.equal(logic.buildListQuery({ status: 'active', regime: '', dormitoryId: '', roomId: '', search: '' }, 1, 20), 'status=active&page=1&pageSize=20');
    assert.equal(
        logic.buildListQuery({ status: 'onLeave', regime: 'Interne', dormitoryId: 'd1', roomId: 'r1', search: ' Awa Diop ' }, 3, 20),
        'dormitoryId=d1&roomId=r1&regime=Interne&status=onLeave&search=Awa%20Diop&page=3&pageSize=20');
    assert.match(logic.buildListQuery({ status: 'active' }, 0, 20), /page=1/, 'une page invalide retombe sur 1');
});

test('STATUS_CHOICES : les quatre statuts de l\'API, en français', () => {
    const { logic } = boot();
    assert.deepEqual(plain(logic.STATUS_CHOICES.map((s) => s.value)), ['active', 'awaitingBed', 'onLeave', 'ended']);
    assert.ok(logic.STATUS_CHOICES.every((s) => s.label));
});

test('buildProfileBody : medicalNotes n\'est JAMAIS dans le corps pour un rôle sans droit médical', () => {
    const { logic } = boot();
    const form = { medicalNotes: 'Allergie', emergencyContactName: ' Fatou ', emergencyContactPhone: '', allowedExitPersons: [{ name: ' Fatou Diop ', relationship: 'Mère', phone: '77' }] };

    const secretariat = logic.buildProfileBody(form, 11, 'Secretariat');
    assert.equal('medicalNotes' in secretariat, false, 'la clé est absente, pas seulement nulle');

    for (const role of ['Directeur', 'Surveillant']) {
        const body = logic.buildProfileBody(form, 11, role);
        assert.equal(body.medicalNotes, 'Allergie', role);
    }
});

test('buildProfileBody : rowVersion 0 est un jeton valide, champs vides en null, personnes nettoyées', () => {
    const { logic } = boot();
    const body = logic.buildProfileBody(
        { medicalNotes: '', emergencyContactName: ' Fatou ', emergencyContactPhone: '  ', allowedExitPersons: [{ name: ' Fatou Diop ', relationship: ' Mère ', phone: ' 77 ' }] }, 0, 'Directeur');
    assert.equal(body.rowVersion, 0);
    assert.equal(body.emergencyContactName, 'Fatou');
    assert.equal(body.emergencyContactPhone, null);
    assert.equal(body.medicalNotes, null, 'fiche vidée = effacée côté serveur');
    assert.deepEqual(plain(body.allowedExitPersons), [{ name: 'Fatou Diop', relationship: 'Mère', phone: '77' }]);
});

test('validateProfile : dix personnes habilitées au plus, un nom obligatoire par personne', () => {
    const { logic } = boot();
    const person = (n) => ({ name: `P${n}`, relationship: '', phone: '' });
    assert.deepEqual(plain(logic.validateProfile({ allowedExitPersons: Array.from({ length: 10 }, (_, i) => person(i)) })), {});
    assert.ok(logic.validateProfile({ allowedExitPersons: Array.from({ length: 11 }, (_, i) => person(i)) }).allowedExitPersons);
    assert.ok(logic.validateProfile({ allowedExitPersons: [{ name: '  ', relationship: '', phone: '' }] }).allowedExitPersons);
    assert.ok(logic.validateProfile({ medicalNotes: 'x'.repeat(2001), allowedExitPersons: [] }).medicalNotes);
});

test('statusBadge : un séjour clos, en attente de lit ou en permission est signalé', () => {
    const { logic } = boot();
    assert.equal(logic.statusLabel(ITEM('a', 'A')), 'Actif');
    assert.equal(logic.statusLabel(ITEM('a', 'A', { isActive: false, endDate: '2026-12-01' })), 'Séjour clos');
    assert.equal(logic.statusLabel(ITEM('a', 'A', { bedId: null })), 'En attente de lit');
    assert.equal(logic.statusLabel(ITEM('a', 'A', { regime: 'DemiPensionnaire', bedId: null })), 'Actif', 'un demi-pensionnaire n\'attend pas de lit');
});

// ---------------------------------------------------------------- composant : liste

test('le chargement initial appelle /boarding/boarders (statut actif) et jamais /internat/*', async () => {
    const { calls, tab } = boot();
    const t = await ready(tab());

    const listCall = calls.find((c) => c.endpoint.startsWith('/boarding/boarders?'));
    assert.ok(listCall, 'liste demandée');
    assert.match(listCall.endpoint, /status=active/);
    assert.ok(calls.every((c) => !c.endpoint.startsWith('/internat')), 'aucune route héritée');
    assert.equal(t.items.length, 2);
    assert.equal(t.totalCount, 2);
});

test('le filtre « en permission » envoie status=onLeave et revient en page 1', async () => {
    const { calls, tab } = boot();
    const t = await ready(tab());
    t.page = 3;
    t.filters.status = 'onLeave';
    await t.applyFilters();

    const last = calls.filter((c) => c.endpoint.startsWith('/boarding/boarders?')).at(-1);
    assert.match(last.endpoint, /status=onLeave/);
    assert.match(last.endpoint, /page=1(&|$)/);
    assert.equal(t.page, 1);
});

test('pagination : page suivante, bornes respectées', async () => {
    const { calls, tab } = boot({ list: { items: [ITEM('s1', 'Awa')], totalCount: 45, page: 1, pageSize: 20 } });
    const t = await ready(tab());

    assert.equal(t.pageCount, 3);
    assert.equal(t.hasPrevious, false);
    assert.equal(t.hasNext, true);

    await t.goToPage(2);
    assert.match(calls.filter((c) => c.endpoint.startsWith('/boarding/boarders?')).at(-1).endpoint, /page=2/);

    const before = calls.length;
    await t.goToPage(9);
    await t.goToPage(0);
    assert.equal(calls.length, before, 'une page hors bornes ne déclenche aucun appel');
});

test("une réponse de liste périmée n'écrase pas la plus récente", async () => {
    const resolvers = [];
    let hold = false;
    const { tab } = boot({
        onGet: (endpoint) => (hold && endpoint.startsWith('/boarding/boarders?')
            ? new Promise((resolve) => resolvers.push(resolve))
            : undefined)
    });
    const t = await ready(tab());

    hold = true;
    const first = t.load();
    const second = t.load();
    resolvers[1]({ items: [ITEM('new', 'Récent')], totalCount: 1, page: 1, pageSize: 20 });
    await second;
    resolvers[0]({ items: [ITEM('old', 'Périmé')], totalCount: 1, page: 1, pageSize: 20 });
    await first;

    assert.deepEqual(plain(t.items.map((i) => i.id)), ['new']);
});

test('un pavillon choisi charge ses chambres ; changer de pavillon réinitialise la chambre', async () => {
    const { calls, tab } = boot();
    const t = await ready(tab());

    t.filters.dormitoryId = 'd1';
    await t.onDormitoryChange();
    assert.ok(calls.some((c) => c.endpoint === '/boarding/dormitories/d1'));
    assert.deepEqual(plain(t.rooms.map((r) => r.id)), ['r1', 'r2']);

    t.filters.roomId = 'r2';
    t.filters.dormitoryId = '';
    await t.onDormitoryChange();
    assert.equal(t.filters.roomId, '');
    assert.deepEqual(plain(t.rooms), []);
});

test('erreur de chargement : message affiché, pas de liste fantôme', async () => {
    const { tab } = boot({ fail: (m, e) => (e.startsWith('/boarding/boarders?') ? apiError('MODULE_DISABLED', 403) : undefined) });
    const t = await ready(tab());
    assert.match(t.error, /Internat/);
    assert.equal(t.loading, false);
});

// ---------------------------------------------------------------- composant : fiche et profil

test('ouvrir la fiche appelle GET /boarding/boarders/{id}', async () => {
    const { calls, tab } = boot();
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));

    assert.ok(calls.some((c) => c.method === 'GET' && c.endpoint === '/boarding/boarders/s1'));
    assert.equal(t.panel.open, true);
    assert.equal(t.panel.data.medicalNotes, 'Asthme — ventoline dans le sac.');
});

test('Secretariat : le champ médical est masqué et le PUT ne contient pas medicalNotes', async () => {
    const { calls, tab } = boot({ role: 'Secretariat', detail: DETAIL({ medicalNotes: null }) });
    const t = await ready(tab());
    assert.equal(t.showMedical, false, 'le gabarit n\'affiche pas la section (x-if)');

    await t.openDetail(ITEM('s1', 'Awa Diop'));
    t.startEdit();
    t.panel.form.medicalNotes = 'Tentative de saisie';
    t.panel.form.emergencyContactName = 'Nouveau contact';
    await t.saveProfile();

    const put = calls.find((c) => c.method === 'PUT');
    assert.ok(put, 'PUT envoyé');
    assert.equal(put.endpoint, '/boarding/boarders/s1/profile');
    assert.equal('medicalNotes' in put.body, false);
    assert.equal(put.body.emergencyContactName, 'Nouveau contact');
    assert.equal(put.body.rowVersion, 11, 'jeton du séjour');
});

test('Directeur et Surveillant : le champ médical est affiché et envoyé', async () => {
    for (const role of ['Directeur', 'Surveillant']) {
        const { calls, tab } = boot({ role });
        const t = await ready(tab());
        assert.equal(t.showMedical, true, role);

        await t.openDetail(ITEM('s1', 'Awa Diop'));
        t.startEdit();
        assert.equal(t.panel.form.medicalNotes, 'Asthme — ventoline dans le sac.', role);
        t.panel.form.medicalNotes = 'Asthme léger';
        await t.saveProfile();

        assert.equal(calls.find((c) => c.method === 'PUT').body.medicalNotes, 'Asthme léger', role);
    }
});

test('medicalNotes null de l\'API : libellé « Non renseignée », jamais « null »', async () => {
    const { tab } = boot({ role: 'Directeur', detail: DETAIL({ medicalNotes: null }) });
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));

    assert.equal(t.medicalText, 'Non renseignée');
    assert.doesNotMatch(String(t.medicalText), /null/);
    t.startEdit();
    assert.equal(t.panel.form.medicalNotes, '', 'le champ de saisie est vide, pas « null »');
});

test('enregistrer le profil recharge la fiche avec la réponse et ferme l\'édition', async () => {
    const updated = DETAIL({ emergencyContactName: 'Mamadou Diop', boarder: ITEM('s1', 'Awa Diop', { rowVersion: 12 }) });
    const { tab } = boot({ detail: updated });
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));
    t.startEdit();
    await t.saveProfile();

    assert.equal(t.panel.editing, false);
    assert.equal(t.panel.data.emergencyContactName, 'Mamadou Diop');
    assert.equal(t.panel.data.boarder.rowVersion, 12);
    assert.match(t.notice, /enregistrée/);
});

test('personnes habilitées : ajout plafonné à dix, retrait', async () => {
    const { tab } = boot();
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));
    t.startEdit();

    while (t.panel.form.allowedExitPersons.length < 10) t.addPerson();
    assert.equal(t.panel.form.allowedExitPersons.length, 10);
    assert.equal(t.canAddPerson, false);
    t.addPerson();
    assert.equal(t.panel.form.allowedExitPersons.length, 10, 'jamais plus de dix');

    t.removePerson(0);
    assert.equal(t.panel.form.allowedExitPersons.length, 9);
    assert.equal(t.canAddPerson, true);
});

test("CONCURRENCY_CONFLICT : message clair, fiche relue, édition fermée (jamais d'écrasement du travail d'un autre)", async () => {
    let fail = true;
    const { calls, tab } = boot({ fail: (m) => (m === 'PUT' && fail ? apiError('CONCURRENCY_CONFLICT') : undefined) });
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));
    t.startEdit();
    await t.saveProfile();

    assert.match(t.panel.error, /modifié par une autre personne/);
    assert.equal(calls.filter((c) => c.method === 'GET' && c.endpoint === '/boarding/boarders/s1').length, 2, 'fiche relue');
    fail = false;
});

test('403 géré par la modale globale : aucune erreur doublée, le formulaire reste ouvert', async () => {
    const { tab } = boot({ fail: (m) => (m === 'PUT' ? apiError('FORBIDDEN', 403, true) : undefined) });
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));
    t.startEdit();
    t.panel.form.emergencyContactName = 'Saisie à ne pas perdre';
    await t.saveProfile();

    assert.equal(t.panel.error, '');
    assert.equal(t.panel.editing, true);
    assert.equal(t.panel.form.emergencyContactName, 'Saisie à ne pas perdre');
    assert.equal(t.panel.saving, false);
});

test('un formulaire invalide n\'envoie rien', async () => {
    const { calls, tab } = boot();
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));
    t.startEdit();
    t.addPerson(); // nom vide
    await t.saveProfile();

    assert.equal(calls.some((c) => c.method === 'PUT'), false);
    assert.ok(t.panel.errors.allowedExitPersons);
});

test('fermer la fiche : plus d\'édition ni de données', async () => {
    const { tab } = boot();
    const t = await ready(tab());
    await t.openDetail(ITEM('s1', 'Awa Diop'));
    t.startEdit();
    t.closeDetail();

    assert.equal(t.panel.open, false);
    assert.equal(t.panel.editing, false);
});

test('options des filtres : « Tous » en tête, au format {value, label} attendu par select-field', async () => {
    const { tab } = boot();
    const t = await ready(tab());
    t.filters.dormitoryId = 'd1';
    await t.onDormitoryChange();

    assert.deepEqual(plain(t.dormitoryOptions), [
        { value: '', label: 'Tous les pavillons' },
        { value: 'd1', label: 'Pavillon Oustaz Ahmad' },
        { value: 'd2', label: 'Pavillon Fatima' }
    ]);
    assert.deepEqual(plain(t.roomOptions.map((o) => o.value)), ['', 'r1', 'r2']);
});

test('échec du chargement des pavillons ou des chambres : signalé sous les filtres, la liste reste utilisable', async () => {
    const { tab } = boot({ fail: (m, e) => (e === '/boarding/dormitories' || e === '/boarding/dormitories/d1' ? Object.assign(new Error(''), { status: 500 }) : undefined) });
    const t = await ready(tab());

    assert.match(t.filterError, /pavillons/i, 'un filtre vide ne doit pas passer pour « aucun pavillon »');
    assert.equal(t.items.length, 2, 'la liste, elle, est chargée');

    t.filters.dormitoryId = 'd1';
    await t.onDormitoryChange();
    assert.match(t.filterError, /chambres/i);
    assert.equal(t.items.length, 2);
});

test('le message des filtres disparaît dès que le chargement réussit', async () => {
    let fail = true;
    const { tab } = boot({ fail: (m, e) => (fail && e === '/boarding/dormitories' ? apiError('MODULE_DISABLED', 500) : undefined) });
    const t = await ready(tab());
    assert.ok(t.filterError);

    fail = false;
    await t.loadDormitories();
    assert.equal(t.filterError, '');
});
