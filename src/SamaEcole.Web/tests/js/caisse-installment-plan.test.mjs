/**
 * Échéancier personnalisé depuis la Caisse (`caisseView` + wwwroot/js/installment-plan.js).
 *
 *   1. L'éditeur part du calendrier actuel ; le total et l'écart au montant dû se recalculent en direct.
 *   2. « Répartir en parts égales » donne une somme EXACTE (le reliquat va à la première part), une échéance par mois.
 *   3. L'enregistrement n'est possible que plan équilibré ; il part avec les bons champs puis recharge le solde.
 *   4. Un refus serveur (somme ≠ montant dû…) reste affiché, l'éditeur reste ouvert.
 *   5. Seuls Directeur et Finance voient le bouton (le Secrétariat encaisse, il ne renégocie pas).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function boot({ role = 'Directeur', post = async () => ({}) } = {}) {
    const calls = { post: [], reload: 0 };
    const api = {
        post: async (endpoint, body) => { calls.post.push({ endpoint, body: plain(body) }); return post(endpoint, body); },
        get: async () => [],
        toMessage: (e, fb) => (e && e.message) || fb,
        toFieldErrors: (e, fb) => (e && e.fields ? e.fields : { global: fb })
    };
    const ctx = loadScripts(['payment-cancel.js', 'installment-plan.js', 'caisse.js'], {
        preload: {
            auth: { role },
            api,
            pdfPreview: { state: () => ({}) },
            networkGuard: { newIdempotencyKey: () => 'key' },
            formatFCFA: (n) => String(n)
        }
    });
    const view = ctx.initAlpine().get('caisseView')();
    view.balance = {
        enrollmentId: 'enr-1', studentFullName: 'Awa Fall', totalDue: 100000,
        installments: [
            { designation: 'Inscription', amount: 40000, dueDate: '2026-10-01T00:00:00+00:00' },
            { designation: 'Scolarité', amount: 60000, dueDate: '2026-11-01T00:00:00+00:00' }
        ]
    };
    view.reloadBalance = async () => { calls.reload += 1; };
    return { view, calls };
}

test('l\'éditeur part du calendrier actuel et signale un plan équilibré', () => {
    const { view } = boot();
    view.openPlanEditor();

    assert.equal(view.plan.open, true);
    assert.equal(view.plan.lines.length, 2);
    assert.equal(view.plan.lines[0].dueDate, '2026-10-01');
    assert.equal(view.planTotal(), 100000);
    assert.equal(view.planGap(), 0);
    assert.equal(view.planCanSubmit(), true);
});

test('modifier un montant déséquilibre le plan : écart signalé, enregistrement bloqué', () => {
    const { view } = boot();
    view.openPlanEditor();
    view.plan.lines[0].amount = 30000;

    assert.equal(view.planGap(), 10000);
    assert.equal(view.planCanSubmit(), false);
});

test('la répartition en parts égales est exacte et mensuelle', () => {
    const { view } = boot();
    view.openPlanEditor();
    view.balance.totalDue = 100000;
    view.plan.splitCount = 3;

    view.splitPlanEvenly();

    assert.equal(JSON.stringify(view.plan.lines.map(l => l.amount)), JSON.stringify([33334, 33333, 33333]));
    assert.equal(view.planGap(), 0);
    const months = view.plan.lines.map(l => l.dueDate.slice(0, 7));
    assert.equal(new Set(months).size, 3, 'une échéance par mois');
});

test('ajouter une ligne propose le reliquat ; on ne dépasse pas 24 échéances', () => {
    const { view } = boot();
    view.openPlanEditor();
    view.plan.lines[1].amount = 50000;

    view.addPlanLine();
    assert.equal(view.plan.lines[2].amount, 10000);

    for (let i = 0; i < 30; i++) view.addPlanLine();
    assert.equal(view.plan.lines.length, 24);
});

test('l\'enregistrement envoie le plan, recharge le solde et referme l\'éditeur', async () => {
    const { view, calls } = boot();
    view.openPlanEditor();
    view.plan.reason = '  Accord signé  ';

    await view.submitPlan();

    assert.deepEqual(calls.post, [{
        endpoint: '/finance/enrollments/enr-1/installment-plan',
        body: {
            reason: 'Accord signé',
            installments: [
                { label: 'Inscription', amount: 40000, dueDate: '2026-10-01' },
                { label: 'Scolarité', amount: 60000, dueDate: '2026-11-01' }
            ]
        }
    }]);
    assert.equal(calls.reload, 1);
    assert.equal(view.plan.open, false);
});

test('un plan déséquilibré ne part jamais vers le serveur', async () => {
    const { view, calls } = boot();
    view.openPlanEditor();
    view.plan.lines[0].amount = 1;

    await view.submitPlan();

    assert.equal(calls.post.length, 0);
});

test('un refus serveur reste affiché et laisse l\'éditeur ouvert', async () => {
    const { view } = boot({ post: async () => { throw { fields: { installments: 'La somme des échéances doit être égale au montant dû.' } }; } });
    view.openPlanEditor();

    await view.submitPlan();

    assert.equal(view.plan.open, true);
    assert.match(view.plan.errors.installments, /doit être égale/);
    assert.equal(view.plan.isSaving, false);
});

test('seuls Directeur et Finance peuvent personnaliser un échéancier', () => {
    assert.equal(boot({ role: 'Directeur' }).view.canManagePlan, true);
    assert.equal(boot({ role: 'Finance' }).view.canManagePlan, true);
    assert.equal(boot({ role: 'Secretariat' }).view.canManagePlan, false);
});
