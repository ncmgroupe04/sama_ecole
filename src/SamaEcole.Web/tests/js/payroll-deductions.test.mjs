/**
 * Paie sans retenues (wwwroot/js/payroll.js) — le Directeur d'un Daara choisit, contrat par contrat, de ne retenir ni IPRES, ni CSS,
 * ni VRS/CFCE, ni BRS. Le régime avec retenues reste le défaut : les deux coexistent dans une même école.
 *
 * Règles verrouillées ici : « avec retenues » par défaut à la création ; la case décochée envoie explicitement `false` ; à la
 * modification, un contrat renvoyé sans le champ (ancienne réponse) reste « avec retenues » — jamais l'inverse ; la valeur
 * du contrat est renvoyée telle quelle au serveur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

function payrollView() {
    const calls = [];
    const api = {
        get: async () => [],
        post: async (url, body) => { calls.push({ method: 'POST', url, body }); return {}; },
        patch: async (url, body) => { calls.push({ method: 'PATCH', url, body }); return {}; },
        toMessage: (err, fallback) => fallback
    };
    const toast = { success() {}, error() {} };
    const ctx = loadScripts(['payroll.js'], {
        preload: { auth: { role: 'Directeur', schoolId: 'school-1' }, api, pdfPreview: { state: () => ({}) } }
    });
    // payroll.js appelle `api` et `toast` comme des globales nues : on les pose sur le bac à sable.
    ctx.sandbox.api = api;
    ctx.sandbox.toast = toast;
    const view = ctx.component('payrollView');
    return { view, calls };
}

test('un nouveau contrat est « avec retenues » par défaut : le calcul historique ne change pas', () => {
    const { view } = payrollView();
    view.openContractModal();

    assert.equal(view.contractForm.applyStatutoryDeductions, true);
});

test('un contrat créé avec la case cochée envoie applyStatutoryDeductions = true', async () => {
    const { view, calls } = payrollView();
    view.openContractModal();
    Object.assign(view.contractForm, { employeeKind: 'external', externalFullName: 'Oustaz Moussa', type: 'Permanent', baseSalary: '40000' });

    await view.submitContract();
    await flush();

    const post = calls.find((c) => c.url === '/finance/employee-contracts');
    assert.ok(post, 'le contrat doit être envoyé');
    assert.equal(post.body.applyStatutoryDeductions, true);
});

test('un contrat créé SANS retenues envoie explicitement applyStatutoryDeductions = false', async () => {
    const { view, calls } = payrollView();
    view.openContractModal();
    Object.assign(view.contractForm, {
        employeeKind: 'external', externalFullName: 'Oustaz Moussa', type: 'Permanent', baseSalary: '40000',
        applyStatutoryDeductions: false
    });

    await view.submitContract();
    await flush();

    const post = calls.find((c) => c.url === '/finance/employee-contracts');
    assert.equal(post.body.applyStatutoryDeductions, false);
    assert.equal(post.body.baseSalary, 40000, 'le salaire est envoyé tel quel');
});

test('modifier un contrat « sans retenues » préremplit la case décochée', () => {
    const { view } = payrollView();
    view.openEditContractModal({
        id: 'c-1', type: 'Permanent', baseSalary: 40000, hourlyRate: 0, transportAllowance: 0,
        payoutMethod: 'Cash', payoutAccountReference: null, rowVersion: 7, applyStatutoryDeductions: false
    });

    assert.equal(view.editContractForm.applyStatutoryDeductions, false);
});

test('un contrat renvoyé sans le champ reste « avec retenues » à la modification, jamais l\'inverse', () => {
    const { view } = payrollView();
    view.openEditContractModal({
        id: 'c-2', type: 'Permanent', baseSalary: 250000, hourlyRate: 0, transportAllowance: 0,
        payoutMethod: 'Cash', payoutAccountReference: null, rowVersion: 3
    });

    assert.equal(view.editContractForm.applyStatutoryDeductions, true);
});

test('la modification renvoie le régime choisi avec le motif et le jeton de version', async () => {
    const { view, calls } = payrollView();
    view.openEditContractModal({
        id: 'c-3', type: 'Permanent', baseSalary: 40000, hourlyRate: 0, transportAllowance: 0,
        payoutMethod: 'Cash', payoutAccountReference: null, rowVersion: 9, applyStatutoryDeductions: true
    });
    view.editContractForm.applyStatutoryDeductions = false;
    view.editContractForm.reason = 'Passage en paie sans retenues.';

    await view.submitEditContract();
    await flush();

    const patch = calls.find((c) => c.method === 'PATCH');
    assert.ok(patch, 'la modification doit être envoyée');
    assert.equal(patch.url, '/finance/employee-contracts/c-3');
    assert.equal(patch.body.applyStatutoryDeductions, false);
    assert.equal(patch.body.rowVersion, 9);
    assert.equal(patch.body.reason, 'Passage en paie sans retenues.');
});
