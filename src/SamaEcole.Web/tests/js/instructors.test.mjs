/**
 * Gestion des Oustaz (/oustaz) : logique pure (`window.instructorsLogic`) et composant `instructorsPage`
 * (wwwroot/js/instructors.js). Le serveur garde l'accès et les règles de liaison (rôle, unicité du compte, 404 neutre) ;
 * ces tests prouvent que l'écran n'envoie que ce qu'il doit, n'appelle jamais GET /users hors Directeur, relit la liste
 * sur conflit, et ne propose que des comptes utilisables.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const INSTRUCTORS = [
    { id: 'i1', fullName: 'Serigne Modou', fullNameAr: 'سيرين مودو', phone: '77 123 45 67', status: 'Active', userId: 'u1', userEmail: 'modou@daara.sn', studentCount: 12, rowVersion: 11 },
    { id: 'i2', fullName: 'Oustaz Fall', fullNameAr: null, phone: null, status: 'Suspended', userId: null, userEmail: null, studentCount: 3, rowVersion: 22 },
    { id: 'i3', fullName: 'Cheikh Ba', fullNameAr: null, phone: '70 000 00 00', status: 'Blocked', userId: 'u3', userEmail: 'ba@daara.sn', studentCount: 0, rowVersion: 33 }
];

const USERS = [
    { id: 'u1', fullName: 'Modou', email: 'modou@daara.sn', role: 'Enseignant', status: 'Active' },   // déjà lié à i1
    { id: 'u2', fullName: 'Awa', email: 'awa@daara.sn', role: 'Enseignant', status: 'Active' },       // libre
    { id: 'u3', fullName: 'Ba', email: 'ba@daara.sn', role: 'Enseignant', status: 'Active' },         // déjà lié à i3
    { id: 'u4', fullName: 'Directrice', email: 'dir@daara.sn', role: 'Directeur', status: 'Active' }, // mauvais rôle
    { id: 'u5', fullName: 'Bloqué', email: 'blo@daara.sn', role: 'Enseignant', status: 'Blocked' }    // compte bloqué
];

function boot(role = 'Directeur', options = {}) {
    const calls = [];
    const ctx = loadScripts(['instructors.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/internat/instructors') {
                        if (options.listError) throw options.listError;
                        return structuredClone(options.instructors ?? INSTRUCTORS);
                    }
                    if (endpoint === '/users') {
                        if (options.usersError) throw options.usersError;
                        return structuredClone(USERS);
                    }
                    return null;
                },
                post: async (endpoint, body) => {
                    calls.push({ method: 'POST', endpoint, body: plain(body) });
                    if (options.writeError) throw options.writeError;
                    return {};
                },
                put: async (endpoint, body) => {
                    calls.push({ method: 'PUT', endpoint, body: plain(body) });
                    if (options.writeError) throw options.writeError;
                    return {};
                },
                // Même contrat que api.js : clés en minuscules, sinon un message global.
                toFieldErrors: (err, fallback) => {
                    if (err && err.details) {
                        return Object.fromEntries(Object.entries(err.details).map(([k, v]) => [k.toLowerCase(), Array.isArray(v) ? v[0] : v]));
                    }
                    return { global: (err && err.message) || fallback };
                },
                toMessage: (err, fallback) => (err && err.message) || fallback
            }
        }
    });
    const components = ctx.initAlpine();
    return { page: components.get('instructorsPage')(), logic: ctx.window.instructorsLogic, calls };
}

function httpError(status, extra = {}) {
    return Object.assign(new Error('HTTP ' + status), { status }, extra);
}

// ---------------------------------------------------------------------------------------------- logique pure

test('la liste se filtre par nom, nom arabe, téléphone, compte et statut', () => {
    const { logic } = boot();
    const ids = (list) => logic.filterInstructors(INSTRUCTORS, ...list).map((i) => i.id);

    assert.deepEqual(ids(['', '']), ['i1', 'i2', 'i3']);
    assert.deepEqual(ids(['modou', '']), ['i1'], 'nom, sans tenir compte de la casse');
    assert.deepEqual(ids(['سيرين', '']), ['i1'], 'nom arabe');
    assert.deepEqual(ids(['70 000', '']), ['i3'], 'téléphone');
    assert.deepEqual(ids(['ba@daara', '']), ['i3'], 'compte de connexion');
    assert.deepEqual(ids(['', 'Suspended']), ['i2']);
    assert.deepEqual(ids(['fall', 'Active']), [], 'les deux critères se cumulent');
    assert.deepEqual(plain(logic.filterInstructors(null, 'x', '')), [], 'une liste absente ne casse pas l\'écran');
});

test('seuls les comptes Enseignant actifs et non déjà liés sont proposés', () => {
    const { logic } = boot();
    const ids = (own) => logic.eligibleAccounts(USERS, INSTRUCTORS, own).map((u) => u.id);

    assert.deepEqual(ids(null), ['u2'], 'u1 et u3 sont pris, u4 n\'est pas Enseignant, u5 est bloqué');
});

test('le compte déjà lié à la fiche en cours reste proposé, pour pouvoir le conserver', () => {
    const { logic } = boot();
    const ids = logic.eligibleAccounts(USERS, INSTRUCTORS, 'u1').map((u) => u.id);

    assert.deepEqual(ids, ['u1', 'u2']);
});

test('à la création, le corps ne porte ni statut ni jeton, et les champs vides deviennent null', () => {
    const { logic } = boot();
    const body = plain(logic.createBody({ fullName: '  Serigne Modou  ', fullNameAr: '  ', phone: '', userId: '' }));

    assert.deepEqual(body, { fullName: 'Serigne Modou', fullNameAr: null, phone: null, userId: null });
});

test('à la modification, la fiche entière part avec le statut et le jeton lu', () => {
    const { logic } = boot();
    const body = plain(logic.updateBody({ fullName: 'A', fullNameAr: 'ا', phone: '77', userId: 'u2', status: 'Suspended', rowVersion: 11 }));

    assert.deepEqual(body, { fullName: 'A', fullNameAr: 'ا', phone: '77', userId: 'u2', status: 'Suspended', rowVersion: 11 });
});

test('un changement de statut renvoie la fiche telle quelle, avec le nouveau statut', () => {
    const { logic } = boot();
    const body = plain(logic.statusChangeBody(INSTRUCTORS[0], 'Suspended'));

    assert.deepEqual(body, { fullName: 'Serigne Modou', fullNameAr: 'سيرين مودو', phone: '77 123 45 67', userId: 'u1', status: 'Suspended', rowVersion: 11 });
    assert.equal(plain(logic.statusChangeBody(INSTRUCTORS[1], 'Active')).userId, null, 'une fiche sans compte reste sans compte');
});

test('les statuts s\'affichent en français', () => {
    const { logic } = boot();
    assert.equal(logic.statusLabel('Active'), 'Actif');
    assert.equal(logic.statusLabel('Suspended'), 'Suspendu');
    assert.equal(logic.statusLabel('Blocked'), 'Bloqué');
});

// ---------------------------------------------------------------------------------------------- chargement et rôles

test('le Directeur charge les Oustaz et les comptes rattachables', async () => {
    const { page, calls } = boot('Directeur');
    await page.init();

    assert.equal(page.canWrite, true);
    assert.equal(page.instructors.length, 3);
    assert.ok(calls.some((c) => c.endpoint === '/users'));
    assert.equal(page.loading, false);
    assert.equal(page.activeCount, 1);
    assert.equal(page.totalStudents, 15);
});

test('le Secrétariat voit la liste sans actions et n\'appelle jamais GET /users', async () => {
    for (const role of ['Secretariat', 'Surveillant']) {
        const { page, calls } = boot(role);
        await page.init();

        assert.equal(page.canWrite, false, role);
        assert.equal(page.instructors.length, 3, role);
        assert.ok(!calls.some((c) => c.endpoint === '/users'), 'GET /users est réservé au Directeur : ' + role);
    }
});

test('une erreur de chargement est affichée', async () => {
    const { page } = boot('Directeur', { listError: httpError(500, { message: 'Panne' }) });
    await page.init();

    assert.equal(page.error, 'Panne');
    assert.equal(page.loading, false);
});

test('GET /users refusé ne bloque pas l\'écran : le sélecteur de compte est simplement vide', async () => {
    const { page } = boot('Directeur', { usersError: httpError(403) });
    await page.init();

    assert.equal(page.error, null);
    assert.deepEqual(plain(page.users), []);
    assert.deepEqual(plain(page.accountOptions), [{ value: '', label: 'Aucun' }]);
});

test('le sélecteur propose « Aucun » puis les comptes utilisables', async () => {
    const { page } = boot('Directeur');
    await page.init();
    page.openCreate();

    assert.deepEqual(plain(page.accountOptions).map((o) => o.value), ['', 'u2']);

    page.openEdit(page.instructors[0]);
    assert.deepEqual(plain(page.accountOptions).map((o) => o.value), ['', 'u1', 'u2'], 'son propre compte reste proposé');
});

// ---------------------------------------------------------------------------------------------- création / modification

test('créer un Oustaz envoie le corps de création puis recharge la liste', async () => {
    const { page, calls } = boot('Directeur');
    await page.init();
    page.openCreate();
    Object.assign(page.form, { fullName: 'Nouvel Oustaz', fullNameAr: '', phone: '77 000 00 00', userId: 'u2' });

    const readsBefore = calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length;
    await page.submitForm();

    const post = calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/internat/instructors');
    assert.deepEqual(post.body, { fullName: 'Nouvel Oustaz', fullNameAr: null, phone: '77 000 00 00', userId: 'u2' });
    assert.equal(page.form.open, false, 'la fenêtre se ferme');
    assert.equal(calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length, readsBefore + 1);
});

test('modifier une fiche renvoie l\'objet entier et le jeton xmin lu', async () => {
    const { page, calls } = boot('Directeur');
    await page.init();
    page.openEdit(page.instructors[0]);
    page.form.fullName = 'Serigne Modou Bamba';
    page.form.userId = '';   // détache le compte
    await page.submitForm();

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/internat/instructors/i1');
    assert.deepEqual(put.body, {
        fullName: 'Serigne Modou Bamba', fullNameAr: 'سيرين مودو', phone: '77 123 45 67',
        userId: null, status: 'Active', rowVersion: 11
    });
});

test('une erreur de champ du serveur reste sous le champ, et la fenêtre reste ouverte', async () => {
    const { page } = boot('Directeur', { writeError: httpError(422, { details: { UserId: ['Ce compte est déjà rattaché à un autre Oustaz.'] } }) });
    await page.init();
    page.openCreate();
    page.form.fullName = 'X';
    page.form.userId = 'u2';
    await page.submitForm();

    assert.equal(page.form.open, true);
    assert.equal(page.form.errors.userid, 'Ce compte est déjà rattaché à un autre Oustaz.');
    assert.equal(page.form.saving, false, 'le bouton redevient actif pour corriger');
});

test('un conflit (409) affiche le message du serveur et relit la liste', async () => {
    const { page, calls } = boot('Directeur', { writeError: httpError(409, { message: 'Cette fiche a été modifiée par une autre personne.' }) });
    await page.init();
    page.openEdit(page.instructors[0]);
    page.form.fullName = 'Autre nom';

    const readsBefore = calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length;
    await page.submitForm();

    assert.equal(page.form.errors.global, 'Cette fiche a été modifiée par une autre personne.');
    assert.equal(page.form.saving, false);
    assert.equal(calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length, readsBefore + 1, 'la liste est relue');
});

test('un double clic sur Enregistrer n\'envoie qu\'une requête', async () => {
    const { page, calls } = boot('Directeur');
    await page.init();
    page.openCreate();
    page.form.fullName = 'X';

    await Promise.all([page.submitForm(), page.submitForm()]);

    assert.equal(calls.filter((c) => c.method === 'POST').length, 1);
});

// ---------------------------------------------------------------------------------------------- suspension

test('suspendre passe par une confirmation, puis envoie la fiche entière avec le nouveau statut', async () => {
    const { page, calls } = boot('Directeur');
    await page.init();

    page.openStatusChange(page.instructors[0], 'Suspended');
    assert.equal(page.confirm.open, true);
    assert.equal(calls.filter((c) => c.method === 'PUT').length, 0, 'rien n\'est envoyé avant la confirmation');

    await page.confirmStatusChange();

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/internat/instructors/i1');
    assert.deepEqual(put.body, {
        fullName: 'Serigne Modou', fullNameAr: 'سيرين مودو', phone: '77 123 45 67',
        userId: 'u1', status: 'Suspended', rowVersion: 11
    });
    assert.equal(page.confirm.open, false);
});

test('réactiver renvoie le statut Active', async () => {
    const { page, calls } = boot('Directeur');
    await page.init();
    page.openStatusChange(page.instructors[1], 'Active');
    await page.confirmStatusChange();

    assert.equal(calls.find((c) => c.method === 'PUT').body.status, 'Active');
});

test('un refus du serveur garde la confirmation ouverte avec son message, et un conflit relit la liste', async () => {
    const { page, calls } = boot('Directeur', { writeError: httpError(409, { message: 'Fiche modifiée.' }) });
    await page.init();
    page.openStatusChange(page.instructors[0], 'Suspended');

    const readsBefore = calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length;
    await page.confirmStatusChange();

    assert.equal(page.confirm.open, true);
    assert.equal(page.confirm.error, 'Fiche modifiée.');
    assert.equal(page.confirm.saving, false);
    assert.equal(calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length, readsBefore + 1);
});
