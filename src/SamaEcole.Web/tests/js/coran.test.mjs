/**
 * Écran Coran (`coranPage`, wwwroot/js/coran.js) — suivi de mémorisation et évaluations orales.
 * On teste la logique : tri « dernière observation », charge utile (champs d'identité jamais renvoyés en correction),
 * validation des bornes, lectures indépendantes et correspondance 409 / 422 → message.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

const same = (actual, expected) => assert.equal(JSON.stringify(actual), JSON.stringify(expected));

function makeView({ role = 'Directeur', api = {} } = {}) {
    const calls = [];
    const ctx = loadScripts(['coran.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (url) => { calls.push(['get', url]); return api.get ? api.get(url) : []; },
                post: async (url, body) => { calls.push(['post', url, body]); return api.post ? api.post(url, body) : null; },
                put: async (url, body) => { calls.push(['put', url, body]); return api.put ? api.put(url, body) : null; },
                toMessage: (err, fallback) => (err && err.message) || fallback
            }
        }
    });
    const view = ctx.component('coranPage');
    calls.length = 0;
    view.calls = calls;
    return view;
}

const STUDENT = { studentId: 's1', fullName: 'Awa Diop', matricule: 'ELEV-1', entries: [] };
const PROGRESS = { id: 'p1', studentId: 's1', juzNumber: 3, hizbNumber: 5, surahNumber: 2, status: 'InProcess', evaluationDate: '2026-11-01', notes: 'RAS', rowVersion: 7 };

test('la dernière observation est la plus récente ; une observation sans date passe en dernier', () => {
    const view = makeView();
    const row = { ...STUDENT, entries: [
        { id: 'a', evaluationDate: null },
        { id: 'b', evaluationDate: '2026-10-01' },
        { id: 'c', evaluationDate: '2026-11-05' }
    ] };

    same(view.sortedEntries(row).map((e) => e.id), ['c', 'b', 'a']);
    assert.equal(view.latestEntry(row).id, 'c');
    assert.equal(view.latestEntry({ entries: [] }), null);
});

test('le résumé annonce « Aucune observation » pour un élève sans historique', () => {
    const view = makeView();
    assert.equal(view.summary(STUDENT), 'Aucune observation');
});

test('seuls le Directeur et l\'Enseignant peuvent saisir ; le Secrétariat consulte', () => {
    assert.equal(makeView({ role: 'Directeur' }).canEnterQuran, true);
    assert.equal(makeView({ role: 'Enseignant' }).canEnterQuran, true);
    assert.equal(makeView({ role: 'Secretariat' }).canEnterQuran, false);
});

test('une modale n\'s\'ouvre pas pour un rôle sans droit d\'écriture', () => {
    const view = makeView({ role: 'Secretariat' });
    view.openCreate(STUDENT);
    assert.equal(view.form.open, false);
});

test('création d\'un suivi : valeurs par défaut sensées et charge utile complète', () => {
    const view = makeView();
    view.activeTab = 'progress';
    view.openCreate(STUDENT);

    assert.equal(view.form.status, 'InProcess');
    assert.equal(view.form.evaluationDate, '');
    Object.assign(view.form, { juzNumber: '3', hizbNumber: '5', surahNumber: '2' });
    same(view.buildPayload(), { studentId: 's1', juzNumber: 3, hizbNumber: 5, surahNumber: 2, status: 'InProcess', evaluationDate: null, notes: null });
});

test('correction d\'un suivi : Juz/Hizb/Sourate et élève ne sont jamais renvoyés, le rowVersion l\'est', () => {
    const view = makeView();
    view.activeTab = 'progress';
    view.openEdit(STUDENT, PROGRESS);
    view.form.status = 'Memorized';

    const payload = view.buildPayload();
    same(payload, { status: 'Memorized', evaluationDate: '2026-11-01', notes: 'RAS', rowVersion: 7 });
    assert.equal('juzNumber' in payload, false);
    assert.equal('studentId' in payload, false);
});

test('évaluation : la date est aujourd\'hui par défaut et la charge utile est numérique', () => {
    const view = makeView();
    view.activeTab = 'evaluations';
    view.openCreate(STUDENT);

    assert.match(view.form.evaluationDate, /^\d{4}-\d{2}-\d{2}$/);
    Object.assign(view.form, { memoryMistakes: '2', tajwidMistakes: '1', hesitations: '0', finalScore: '14.5' });
    const payload = view.buildPayload();
    assert.equal(payload.memoryMistakes, 2);
    assert.equal(payload.finalScore, 14.5);
    assert.equal(payload.studentId, 's1');
});

test('validation des bornes du serveur (Juz 1–30, Hizb 1–60, Sourate 1–114)', () => {
    const view = makeView();
    view.activeTab = 'progress';
    view.openCreate(STUDENT);
    Object.assign(view.form, { juzNumber: '31', hizbNumber: '0', surahNumber: '115' });

    const errors = view.validate();
    assert.ok(errors.juzNumber && errors.hizbNumber && errors.surahNumber);

    Object.assign(view.form, { juzNumber: '30', hizbNumber: '60', surahNumber: '114' });
    same(view.validate(), {});
});

test('évaluation : une note négative ou une date absente sont refusées avant l\'envoi', () => {
    const view = makeView();
    view.activeTab = 'evaluations';
    view.openCreate(STUDENT);
    Object.assign(view.form, { finalScore: '-1', evaluationDate: '' });

    const errors = view.validate();
    assert.ok(errors.finalScore && errors.evaluationDate);
});

test('une saisie invalide n\'appelle pas le serveur', async () => {
    const view = makeView();
    view.activeTab = 'progress';
    view.openCreate(STUDENT);
    await view.submit();

    assert.equal(view.calls.length, 0);
    assert.ok(view.form.errors.juzNumber);
    assert.equal(view.form.open, true);
});

test('une création valide poste puis recharge la classe', async () => {
    const view = makeView({ api: { get: async () => [] } });
    view.selectedClassroomId = 'c1';
    view.activeTab = 'progress';
    view.openCreate(STUDENT);
    Object.assign(view.form, { juzNumber: '1', hizbNumber: '1', surahNumber: '1' });
    await view.submit();

    assert.equal(view.calls[0][0], 'post');
    assert.equal(view.calls[0][1], '/quran/progress');
    assert.equal(view.form.open, false);
    assert.ok(view.calls.some((c) => c[1] === '/quran/progress/classroom/c1'));
});

test('une correction utilise PUT sur l\'identifiant de l\'observation', async () => {
    const view = makeView({ api: { get: async () => [] } });
    view.selectedClassroomId = 'c1';
    view.activeTab = 'progress';
    view.openEdit(STUDENT, PROGRESS);
    await view.submit();

    assert.equal(view.calls[0][0], 'put');
    assert.equal(view.calls[0][1], '/quran/progress/p1');
});

test('409 → message de concurrence ; 422 → message du serveur ; la fenêtre reste ouverte', async () => {
    const conflict = Object.assign(new Error('x'), { status: 409 });
    const view = makeView({ api: { put: async () => { throw conflict; } } });
    view.activeTab = 'progress';
    view.openEdit(STUDENT, PROGRESS);
    await view.submit();

    assert.match(view.form.errors.global, /modifiée par un autre utilisateur/);
    assert.equal(view.form.open, true);
    assert.equal(view.form.saving, false);

    const invalid = Object.assign(new Error('Statut de mémorisation invalide.'), { status: 422 });
    const view2 = makeView({ api: { put: async () => { throw invalid; } } });
    view2.activeTab = 'progress';
    view2.openEdit(STUDENT, PROGRESS);
    await view2.submit();
    assert.equal(view2.form.errors.global, 'Statut de mémorisation invalide.');
});

test('les deux lectures sont indépendantes : l\'échec de l\'une n\'efface pas l\'autre onglet', async () => {
    const view = makeView({ api: { get: async (url) => {
        if (url.includes('/evaluations/')) throw new Error('Évaluations indisponibles');
        return [STUDENT];
    } } });
    view.selectedClassroomId = 'c1';
    await view.loadClassroom();

    assert.equal(view.progressRows.length, 1);
    assert.equal(view.progressError, null);
    assert.equal(view.evaluationsError, 'Évaluations indisponibles');
});

test('changer de classe vide les données précédentes et l\'historique déplié', async () => {
    const view = makeView({ api: { get: async () => [STUDENT] } });
    view.selectedClassroomId = 'c1';
    await view.loadClassroom();
    view.toggle(view.progressRows[0]);
    assert.equal(view.isExpanded(view.progressRows[0]), true);

    view.selectedClassroomId = '';
    await view.loadClassroom();
    same(view.progressRows, []);
    same(view.expanded, {});
});
