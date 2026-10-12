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

// Halqa de i1 (élèves déjà rattachés) et élèves affectables (certains sans Oustaz, un déjà chez i1, un chez un autre Oustaz).
const MEMBERS = [
    { studentId: 'm1', matricule: 'ELEV-0001', fullName: 'Awa Diop', fullNameAr: 'عائشة', completedHizbs: 3, progressPercent: 5.4 },
    { studentId: 'm2', matricule: 'ELEV-0002', fullName: 'Binta Fall', fullNameAr: null, completedHizbs: 0, progressPercent: 0 }
];
const CANDIDATES = [
    { studentId: 'c1', matricule: 'ELEV-0010', fullName: 'Cheikh Ba', fullNameAr: null, classroomName: 'CM1', instructorId: null, instructorName: null },
    { studentId: 'c2', matricule: 'ELEV-0011', fullName: 'Demba Sy', fullNameAr: null, classroomName: 'CM1', instructorId: null, instructorName: null },
    { studentId: 'm1', matricule: 'ELEV-0001', fullName: 'Awa Diop', fullNameAr: 'عائشة', classroomName: 'CM2', instructorId: 'i1', instructorName: 'Serigne Modou' },
    { studentId: 'c3', matricule: 'ELEV-0012', fullName: 'Fatou Ndiaye', fullNameAr: null, classroomName: 'CE2', instructorId: 'i2', instructorName: 'Oustaz Fall' }
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
                    if (/^\/internat\/instructors\/[^/]+\/students$/.test(endpoint)) {
                        if (options.membersError) throw options.membersError;
                        return structuredClone({ instructorId: 'i1', students: options.members ?? MEMBERS });
                    }
                    if (endpoint.startsWith('/internat/assignable-students')) {
                        if (options.candidatesError) throw options.candidatesError;
                        const items = options.candidates ?? CANDIDATES;
                        return structuredClone({ items, totalCount: options.total ?? items.length, page: 1, pageSize: 20 });
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

// ------------------------------------------------------------------------------- affectation des élèves à une Halqa

test('l\'URL de la liste des élèves affectables porte le périmètre, la page et la recherche nettoyée', () => {
    const { logic } = boot();
    assert.equal(logic.assignableUrl({ scope: 'unassigned', search: '', page: 1, pageSize: 20 }),
        '/internat/assignable-students?scope=unassigned&page=1&pageSize=20');
    assert.equal(logic.assignableUrl({ scope: 'all', search: '  Awa Diop ', page: 3, pageSize: 20 }),
        '/internat/assignable-students?scope=all&page=3&pageSize=20&search=Awa%20Diop');
    assert.equal(logic.assignableUrl({ scope: 'all', search: 'عائشة', page: 1, pageSize: 20 }),
        '/internat/assignable-students?scope=all&page=1&pageSize=20&search=' + encodeURIComponent('عائشة'));
});

test('les corps d\'affectation et de retrait sont ceux de POST assign-instructor', () => {
    const { logic } = boot();
    assert.deepEqual(plain(logic.assignBody('i1', ['c1', 'c2'])), { instructorId: 'i1', studentIds: ['c1', 'c2'] });
    assert.deepEqual(plain(logic.detachBody('m2')), { instructorId: null, studentIds: ['m2'] }, 'retirer = détacher, jamais supprimer');
});

test('on prévient quand affecter un élève le déplace d\'une autre Halqa', () => {
    const { logic } = boot();
    assert.equal(logic.moveWarning({ instructorId: null, instructorName: null }, 'i1'), null);
    assert.equal(logic.moveWarning({ instructorId: 'i1', instructorName: 'Serigne Modou' }, 'i1'), null, 'déjà dans cette Halqa');
    assert.equal(logic.moveWarning({ instructorId: 'i2', instructorName: 'Oustaz Fall' }, 'i1'), 'Actuellement chez Oustaz Fall : sera déplacé.');
});

test('le nombre de pages suit le total et la taille de page', () => {
    const { logic } = boot();
    assert.equal(logic.pageCount(0, 20), 1);
    assert.equal(logic.pageCount(20, 20), 1);
    assert.equal(logic.pageCount(21, 20), 2);
});

test('« Élèves » ouvre la Halqa : ses élèves actuels et les élèves sans Oustaz, relus au serveur', async () => {
    const { page, calls } = boot();
    await page.init();

    await page.openHalqa(page.instructors[0]);

    assert.equal(page.halqa.open, true);
    assert.ok(calls.some((c) => c.method === 'GET' && c.endpoint === '/internat/instructors/i1/students'));
    assert.ok(calls.some((c) => c.method === 'GET' && c.endpoint === '/internat/assignable-students?scope=unassigned&page=1&pageSize=20'));
    assert.deepEqual(page.halqa.members.map((m) => m.studentId), ['m1', 'm2']);
    assert.equal(page.halqa.candidates.length, 4);
});

test('hors Directeur, l\'écran d\'affectation ne s\'ouvre pas et ne lit rien', async () => {
    const { page, calls } = boot('Secretariat');
    await page.init();
    const readsBefore = calls.length;

    await page.openHalqa(page.instructors[0]);

    assert.equal(page.halqa.open, false);
    assert.equal(calls.length, readsBefore);
});

test('un élève déjà dans cette Halqa n\'est pas sélectionnable, les autres le sont', async () => {
    const { page } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);

    const byId = Object.fromEntries(page.halqa.candidates.map((c) => [c.studentId, c]));
    assert.equal(page.isSelectable(byId.m1), false, 'déjà chez i1');
    assert.equal(page.isSelectable(byId.c1), true);
    assert.equal(page.isSelectable(byId.c3), true, 'chez un autre Oustaz : sélectionnable, avec avertissement');
    page.toggleSelect(byId.m1);
    assert.equal(page.selectedCount, 0, 'un élève non sélectionnable ne se coche pas');
});

test('cocher et décocher met à jour le compteur, et la sélection survit à une nouvelle recherche', async () => {
    const { page } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);
    const [c1, c2] = page.halqa.candidates;

    page.toggleSelect(c1);
    page.toggleSelect(c2);
    assert.equal(page.selectedCount, 2);
    page.toggleSelect(c1);
    assert.equal(page.selectedCount, 1);
    assert.equal(page.isSelected('c2'), true);

    page.halqa.search = 'demba';
    await page.searchCandidates();
    assert.equal(page.isSelected('c2'), true, 'la sélection n\'est pas perdue en cherchant');
});

test('« Tout sélectionner » coche les élèves affectables de la page, et seulement eux', async () => {
    const { page } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);

    page.toggleSelectPage();
    assert.equal(page.selectedCount, 3, 'c1, c2, c3 — pas m1, déjà dans la Halqa');
    page.toggleSelectPage();
    assert.equal(page.selectedCount, 0, 'second appel : tout décocher');
});

test('la case « tout sélectionner » reflète l\'état réel de la page', async () => {
    const { page } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);
    assert.equal(page.pageFullySelected, false);

    page.toggleSelect(page.halqa.candidates[0]);
    assert.equal(page.pageFullySelected, false, 'un seul sur trois');
    page.toggleSelectPage();
    assert.equal(page.pageFullySelected, true);
});

test('affecter envoie les élèves cochés à l\'Oustaz, puis relit la Halqa, la liste et les compteurs', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);
    page.toggleSelect(page.halqa.candidates[0]);
    page.toggleSelect(page.halqa.candidates[1]);
    const instructorReads = () => calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length;
    const before = { instructors: instructorReads(), members: calls.filter((c) => c.endpoint === '/internat/instructors/i1/students').length };

    await page.assignSelected();

    const post = calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/internat/students/assign-instructor');
    assert.deepEqual(post.body, { instructorId: 'i1', studentIds: ['c1', 'c2'] });
    assert.equal(page.selectedCount, 0, 'la sélection est vidée');
    assert.equal(page.halqa.notice, '2 élèves affectés à Serigne Modou.');
    assert.equal(page.halqa.error, null);
    assert.equal(calls.filter((c) => c.endpoint === '/internat/instructors/i1/students').length, before.members + 1, 'la Halqa est relue');
    assert.equal(instructorReads(), before.instructors + 1, 'les effectifs de la liste sont relus');
    assert.equal(page.halqa.saving, false);
});

test('un seul élève affecté s\'écrit au singulier', async () => {
    const { page } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);
    page.toggleSelect(page.halqa.candidates[0]);

    await page.assignSelected();

    assert.equal(page.halqa.notice, '1 élève affecté à Serigne Modou.');
});

test('sans élève coché, rien n\'est envoyé', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);

    await page.assignSelected();

    assert.equal(calls.filter((c) => c.method === 'POST').length, 0);
});

test('au-delà de 200 élèves, l\'envoi est refusé avant l\'appel avec un message clair', async () => {
    const many = Array.from({ length: 201 }, (_, i) => ({ studentId: 's' + i, matricule: 'E' + i, fullName: 'Élève ' + i, fullNameAr: null, classroomName: 'CM1', instructorId: null, instructorName: null }));
    const { page, calls } = boot('Directeur', { candidates: many });
    await page.init();
    await page.openHalqa(page.instructors[0]);
    many.forEach((s) => page.toggleSelect(s));
    assert.equal(page.selectedCount, 201);

    await page.assignSelected();

    assert.equal(calls.filter((c) => c.method === 'POST').length, 0);
    assert.match(page.halqa.error, /200/);
    assert.equal(page.selectedCount, 201, 'la sélection est conservée pour que la Direction l\'allège');
});

test('une erreur du serveur s\'affiche dans la fenêtre et garde la sélection', async () => {
    const { page } = boot('Directeur', { writeError: httpError(422, { message: 'Cet Oustaz n\'est pas actif : réactivez-le avant de lui affecter des élèves.' }) });
    await page.init();
    await page.openHalqa(page.instructors[0]);
    page.toggleSelect(page.halqa.candidates[0]);

    await page.assignSelected();

    assert.equal(page.halqa.error, 'Cet Oustaz n\'est pas actif : réactivez-le avant de lui affecter des élèves.');
    assert.equal(page.halqa.notice, null);
    assert.equal(page.selectedCount, 1);
    assert.equal(page.halqa.saving, false, 'le bouton redevient actif');
    assert.equal(page.error, null, 'l\'erreur de la fenêtre n\'est pas celle de la page');
});

test('un Oustaz suspendu ne peut pas recevoir d\'élèves : l\'écran l\'explique et n\'envoie rien', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openHalqa(page.instructors[1]);   // i2, Suspended
    assert.equal(page.canAssign, false);
    page.toggleSelect(page.halqa.candidates[0]);

    await page.assignSelected();

    assert.equal(calls.filter((c) => c.method === 'POST').length, 0);
});

test('retirer un élève demande confirmation, puis le détache sans le supprimer', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);

    page.askRemove(page.halqa.members[1]);
    assert.equal(page.halqa.pendingRemoval, 'm2');
    assert.equal(calls.filter((c) => c.method === 'POST').length, 0, 'rien n\'est envoyé avant confirmation');

    await page.confirmRemove();

    const post = calls.find((c) => c.method === 'POST');
    assert.equal(post.endpoint, '/internat/students/assign-instructor');
    assert.deepEqual(post.body, { instructorId: null, studentIds: ['m2'] });
    assert.equal(page.halqa.pendingRemoval, null);
    assert.equal(page.halqa.notice, 'Binta Fall n\'est plus dans cette Halqa.');
});

test('annuler un retrait n\'envoie rien', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);
    page.askRemove(page.halqa.members[0]);

    page.cancelRemove();

    assert.equal(page.halqa.pendingRemoval, null);
    assert.equal(calls.filter((c) => c.method === 'POST').length, 0);
});

test('chercher revient à la première page et lit avec le terme ; « Tous les élèves » élargit le périmètre', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);
    page.halqa.page = 3;
    page.halqa.search = 'Awa';

    await page.searchCandidates();
    assert.equal(page.halqa.page, 1);
    assert.ok(calls.some((c) => c.endpoint === '/internat/assignable-students?scope=unassigned&page=1&pageSize=20&search=Awa'));

    page.halqa.scope = 'all';
    await page.searchCandidates();
    assert.ok(calls.some((c) => c.endpoint === '/internat/assignable-students?scope=all&page=1&pageSize=20&search=Awa'));
});

test('changer de page relit la page demandée, bornée au nombre de pages', async () => {
    const { page, calls } = boot('Directeur', { total: 45 });
    await page.init();
    await page.openHalqa(page.instructors[0]);
    assert.equal(page.pageCount, 3);

    await page.goToCandidatePage(2);
    assert.ok(calls.some((c) => c.endpoint === '/internat/assignable-students?scope=unassigned&page=2&pageSize=20'));

    const reads = calls.length;
    await page.goToCandidatePage(9);
    assert.equal(calls.length, reads, 'une page hors bornes ne lit rien');
});

test('une erreur de chargement des élèves affectables s\'affiche dans la fenêtre, la page reste intacte', async () => {
    const { page } = boot('Directeur', { candidatesError: httpError(500, { message: 'Erreur serveur' }) });
    await page.init();

    await page.openHalqa(page.instructors[0]);

    assert.equal(page.candidatesError, 'Erreur serveur');
    assert.equal(page.halqa.candidatesLoading, false);
    assert.deepEqual(plain(page.halqa.candidates), []);
    assert.equal(page.error, null);
    assert.equal(page.instructors.length, 3, 'la liste des Oustaz reste affichée');
});

test('fermer la fenêtre relit la liste des Oustaz pour que les effectifs soient à jour', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openHalqa(page.instructors[0]);
    const reads = () => calls.filter((c) => c.method === 'GET' && c.endpoint === '/internat/instructors').length;
    const before = reads();

    await page.closeHalqa();

    assert.equal(page.halqa.open, false);
    assert.equal(reads(), before + 1);
});
