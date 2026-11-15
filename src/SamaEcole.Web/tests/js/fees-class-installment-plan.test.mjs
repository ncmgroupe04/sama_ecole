/**
 * Échéancier par classe (`feesView`, wwwroot/js/fees.js) : un même modèle (pourcentages + décalages en jours)
 * appliqué à chaque inscription active de la classe — POST /finance/classrooms/{id}/installment-plan.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

function bootFees({ role = 'Directeur', post = async () => ({ appliedCount: 3, skippedCount: 0 }) } = {}) {
    const calls = { post: [], toasts: [] };
    const api = {
        post: async (endpoint, body) => { calls.post.push({ endpoint, body: plain(body) }); return post(endpoint, body); },
        get: async () => [],
        toMessage: (e, fb) => (e && e.message) || fb,
        toFieldErrors: (e, fb) => (e && e.fields ? e.fields : { global: fb })
    };
    const ctx = loadScripts(['guided-delete.js', 'fees.js'], {
        preload: {
            auth: { role, accessToken: 'jwt', isAuthenticated: () => false, isAccessTokenStale: () => false, redirectToLogin() {} },
            api,
            toast: { success: (m) => calls.toasts.push(m), error() {} }
        }
    });
    ctx.sandbox.toast = { success: (m) => calls.toasts.push(m), error() {} };
    return { fees: ctx.initAlpine().get('feesView')(), calls };
}

test('le modèle par défaut (50 % / 50 %) est équilibré et prêt à envoyer une fois la classe choisie', () => {
    const { fees } = bootFees();
    fees.openClassPlan();

    assert.equal(fees.classPlanTotalPercent(), 100);
    assert.equal(fees.classPlanCanSubmit(), false, 'pas de classe choisie');
    fees.classPlan.classroomId = 'c1';
    assert.equal(fees.classPlanCanSubmit(), true);
});

test('un total différent de 100 % bloque l\'envoi', () => {
    const { fees } = bootFees();
    fees.openClassPlan();
    fees.classPlan.classroomId = 'c1';
    fees.classPlan.lines[0].percent = 40;

    assert.equal(fees.classPlanTotalPercent(), 90);
    assert.equal(fees.classPlanCanSubmit(), false);
});

test('la répartition en parts égales totalise exactement 100 % (33,34 + 33,33 + 33,33)', () => {
    const { fees } = bootFees();
    fees.openClassPlan();
    fees.classPlan.splitCount = 3;

    fees.splitClassPlanEvenly();

    assert.equal(JSON.stringify(fees.classPlan.lines.map(l => l.percent)), JSON.stringify([33.34, 33.33, 33.33]));
    assert.equal(JSON.stringify(fees.classPlan.lines.map(l => l.offsetDays)), JSON.stringify([0, 30, 60]));
    assert.equal(fees.classPlanTotalPercent(), 100);
});

test('l\'envoi convertit les pourcentages en fractions et annonce le résultat', async () => {
    const { fees, calls } = bootFees({ post: async () => ({ appliedCount: 12, skippedCount: 2 }) });
    fees.openClassPlan();
    fees.classPlan.classroomId = 'c1';
    fees.classPlan.reason = ' Rentrée ';
    fees.classPlan.lines = [
        { label: 'Acompte', percent: 40, offsetDays: 0 },
        { label: 'Solde', percent: 60, offsetDays: 45 }
    ];

    await fees.submitClassPlan();

    assert.deepEqual(calls.post, [{
        endpoint: '/finance/classrooms/c1/installment-plan',
        body: { reason: 'Rentrée', template: [
            { label: 'Acompte', percentage: 0.4, offsetDays: 0 },
            { label: 'Solde', percentage: 0.6, offsetDays: 45 }
        ] }
    }]);
    assert.equal(fees.classPlan.open, false);
    assert.match(calls.toasts[0], /12 inscriptions/);
    assert.match(calls.toasts[0], /2 ignorées/);
});

test('un refus serveur reste affiché, la modale reste ouverte', async () => {
    const { fees } = bootFees({ post: async () => { throw { fields: { template: 'La somme des pourcentages du modèle doit être exactement 100 %.' } }; } });
    fees.openClassPlan();
    fees.classPlan.classroomId = 'c1';

    await fees.submitClassPlan();

    assert.equal(fees.classPlan.open, true);
    assert.match(fees.classPlan.errors.template, /100 %/);
});

test('Directeur et Finance voient « Échelonner une classe », pas le Secrétariat', () => {
    assert.equal(bootFees({ role: 'Directeur' }).fees.canPlanInstallments, true);
    assert.equal(bootFees({ role: 'Finance' }).fees.canPlanInstallments, true);
    assert.equal(bootFees({ role: 'Secretariat' }).fees.canPlanInstallments, false);
});
