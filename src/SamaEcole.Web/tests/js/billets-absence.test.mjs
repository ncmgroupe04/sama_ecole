/**
 * Onglet « Billets d'absence » de l'écran Billets (absence de plusieurs jours, côté navigateur).
 *
 *   1. La liste se charge depuis GET /absences/justifications et la période se lit « Du … au … » / « Le … ».
 *   2. L'enregistrement envoie la plage (fin vide = null) et recharge la liste ; un refus serveur s'affiche par champ.
 *   3. L'annulation passe par une confirmation puis DELETE /absences/justifications/{id}.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

const LIST = [
    { id: 'j1', studentId: 'awa', studentFullName: 'Awa Fall', studentMatricule: 'E1', startDate: '2026-10-12', endDate: '2026-10-14', days: 3, reason: 'Paludisme', authorizedReturnDate: null },
    { id: 'j2', studentId: 'mod', studentFullName: 'Modou Ba', studentMatricule: 'E2', startDate: '2026-10-20', endDate: '2026-10-20', days: 1, reason: 'Deuil', authorizedReturnDate: '2026-10-21' }
];

async function boot({ postError = null } = {}) {
    const calls = { get: [], post: [], delete: [] };
    const api = {
        get: async (endpoint) => { calls.get.push(endpoint); return endpoint === '/absences/justifications' ? LIST : []; },
        getAllPages: async () => [{ id: 'awa', fullName: 'Awa Fall', matricule: 'E1' }],
        post: async (endpoint, body) => {
            calls.post.push({ endpoint, body: plain(body) });
            if (postError) throw postError;
            return 'new-id';
        },
        delete: async (endpoint) => { calls.delete.push(endpoint); return {}; },
        toMessage: (e, fallback) => (e && e.message) || fallback,
        toFieldErrors: (e, fallback) => (e && e.fields ? e.fields : { global: fallback })
    };
    const ctx = loadScripts(['auth.js', 'pdf-preview.js', 'billets.js'], { preload: { api } });
    ctx.sandbox.api = api;
    ctx.sandbox.toast = { error() {}, success() {} };
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role: 'Surveillant' }), expiresIn: 900 });
    const view = ctx.component('billetsView');
    await flush();
    return { view, calls };
}

test('la liste des billets d\'absence se charge et la période se lit clairement', async () => {
    const { view } = await boot();

    assert.equal(view.absences.length, 2);
    assert.match(view.absencePeriod(LIST[0]), /^Du .* au .*/);
    assert.match(view.absencePeriod(LIST[1]), /^Le /);
    assert.equal(view.absenceDaysLabel(LIST[0]), '3 jours');
    assert.equal(view.absenceDaysLabel(LIST[1]), '1 jour');
});

test('l\'enregistrement envoie la plage (fin vide = null), puis recharge et referme la modale', async () => {
    const { view, calls } = await boot();
    view.openCreateAbsence();
    Object.assign(view.absenceForm, { studentId: 'awa', startDate: '2026-10-12', endDate: '', reason: 'Paludisme' });

    await view.submitCreateAbsence();

    assert.deepEqual(calls.post, [{
        endpoint: '/absences/justifications',
        body: { studentId: 'awa', startDate: '2026-10-12', endDate: null, reason: 'Paludisme', authorizedReturnDate: null }
    }]);
    assert.equal(view.isCreateAbsenceOpen, false);
    assert.equal(calls.get.filter(e => e === '/absences/justifications').length, 2, 'liste rechargée');
});

test('un refus serveur (chevauchement…) reste affiché dans la modale, qui reste ouverte', async () => {
    const { view } = await boot({ postError: { fields: { startdate: 'Cet élève a déjà un billet d\'absence.' } } });
    view.openCreateAbsence();
    Object.assign(view.absenceForm, { studentId: 'awa', startDate: '2026-10-12', reason: 'x' });

    await view.submitCreateAbsence();

    assert.equal(view.isCreateAbsenceOpen, true);
    assert.match(view.createAbsenceErrors.startdate, /déjà un billet/);
    assert.equal(view.isCreatingAbsence, false);
});

test('l\'annulation exige une confirmation puis supprime le billet', async () => {
    const { view, calls } = await boot();

    view.askCancelAbsence(LIST[0]);
    assert.equal(view.absenceToCancel.id, 'j1');
    assert.equal(calls.delete.length, 0, 'rien n\'est supprimé avant la confirmation');

    await view.confirmCancelAbsence();

    assert.deepEqual(calls.delete, ['/absences/justifications/j1']);
    assert.equal(view.absenceToCancel, null);
});

test('fermer la modale de création remet le formulaire à zéro', async () => {
    const { view } = await boot();
    view.openCreateAbsence();
    Object.assign(view.absenceForm, { studentId: 'awa', reason: 'brouillon' });

    view.closeCreateAbsence();

    assert.equal(view.absenceForm.studentId, '');
    assert.equal(view.absenceForm.reason, '');
});

test('« Imprimer le billet » ouvre l\'aperçu PDF du billet d\'absence', async () => {
    const { view } = await boot();
    const opened = [];
    view.openPdfPreview = async (url, title, filename) => { opened.push({ url, title, filename }); };

    await view.printAbsenceBillet('j1');

    assert.deepEqual(opened, [{ url: '/api/v1/billets/absence/j1/pdf', title: "Billet d'absence", filename: 'Billet-Absence-j1.pdf' }]);
    assert.equal(view.printingAbsenceId, null);
});
