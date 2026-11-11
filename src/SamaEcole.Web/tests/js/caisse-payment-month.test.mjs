/**
 * Caisse — option du Directeur « demander le mois réglé » (SchoolSettings.AskPaymentMonthAtCashier).
 *
 * Désactivée : l'écran est EXACTEMENT celui d'avant (contrat verrouillé par caisse-quick-pay.test.mjs).
 * Activée : toutes les échéances dues sont proposées, la sélection est libre (juin sans octobre), et chaque
 * mensualité cochée part avec son mois civil (1-12) dans la ligne de règlement.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function caisseView(askMonth) {
    const ctx = loadScripts(['payment-cancel.js', 'caisse.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            networkGuard: { newIdempotencyKey: () => 'key' },
            formatFCFA: (n) => String(n)
        }
    });
    ctx.sandbox.Alpine.store('schoolConfig', { askPaymentMonthAtCashier: askMonth });
    return ctx.initAlpine().get('caisseView')();
}

/** Inscription + 3 mensualités dont JUIN (échéance éloignée). dueDate en UTC, comme le serveur. */
function balance() {
    return {
        enrollmentId: 'enr-1',
        installments: [
            { id: 'LINE-a', designation: 'Inscription', remainingDue: 10000, isInitialScope: true, isDueNow: true, feeCategoryId: 'c-insc', dueDate: '2026-10-01T00:00:00+00:00' },
            { id: 'MONTH-x-1', designation: 'Mensualité (Mois 1)', remainingDue: 5000, isInitialScope: true, isDueNow: true, feeCategoryId: 'c-scol', dueDate: '2026-10-01T00:00:00+00:00' },
            { id: 'MONTH-x-2', designation: 'Mensualité (Mois 2)', remainingDue: 5000, isInitialScope: false, isDueNow: false, feeCategoryId: 'c-scol', dueDate: '2026-11-01T00:00:00+00:00' },
            { id: 'MONTH-x-9', designation: 'Mensualité (Mois 9)', remainingDue: 5000, isInitialScope: false, isDueNow: false, feeCategoryId: 'c-scol', dueDate: '2027-06-01T00:00:00+00:00' }
        ]
    };
}

test('option désactivée : seules les échéances à régler maintenant sont proposées, sans mois transmis', async () => {
    const v = caisseView(false);
    v.balance = balance();
    assert.deepEqual(v.quickPayRows().map((r) => r.id), ['LINE-a', 'MONTH-x-1']);

    v.openQuickPay();
    let sent = null;
    v.recordPayment = async (p) => { sent = p; return true; };
    await v.submitQuickPay();

    assert.equal(sent.breakdowns.some((b) => 'month' in b), false);
});

test('option activée : toutes les échéances dues sont proposées, seules celles dues maintenant sont pré-cochées', () => {
    const v = caisseView(true);
    v.balance = balance();

    assert.equal(v.quickPayRows().length, 4);
    v.openQuickPay();
    assert.deepEqual(v.quickPay.checked, [true, true, false, false]);
    assert.equal(v.quickPay.amount, 15000, 'un caissier qui ne touche à rien encaisse comme avant');
});

test('option activée : on peut cocher juin sans octobre, et le total suit', () => {
    const v = caisseView(true);
    v.balance = balance();
    v.openQuickPay();

    v.quickPay.checked = [true, false, false, true]; // inscription + juin, sans octobre
    v.onQuickRowToggle(3);

    assert.deepEqual(v.quickPay.checked, [true, false, false, true], 'aucune sélection forcée');
    assert.equal(v.quickPay.amount, 15000);
});

test('option activée : chaque mensualité cochée part avec son mois civil, pas les frais ponctuels', async () => {
    const v = caisseView(true);
    v.balance = balance();
    v.openQuickPay();
    v.quickPay.checked = [true, false, false, true];
    v.quickPay.amount = v.quickPayTotal();
    let sent = null;
    v.recordPayment = async (p) => { sent = p; return true; };

    await v.submitQuickPay();

    assert.equal(sent.breakdowns.length, 2);
    const inscription = sent.breakdowns.find((b) => b.feeCategoryId === 'c-insc');
    const june = sent.breakdowns.find((b) => b.feeCategoryId === 'c-scol');
    assert.equal('month' in inscription, false);
    assert.equal(june.month, 6);
});

test('option activée : le nom du mois est affiché à côté de la mensualité', () => {
    const v = caisseView(true);
    v.balance = balance();
    assert.equal(v.quickPayMonthName(v.balance.installments[3]), 'Juin');
    assert.equal(v.quickPayMonthName(v.balance.installments[0]), '');
});
