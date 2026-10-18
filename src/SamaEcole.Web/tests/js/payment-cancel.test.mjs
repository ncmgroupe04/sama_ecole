/**
 * Annulation d'un paiement (wwwroot/js/payment-cancel.js) — composant PARTAGÉ par la fiche élève et la Caisse.
 *
 * Règles verrouillées ici : le motif est OBLIGATOIRE et se compose d'une liste proposée + une précision (obligatoire
 * pour « Autre motif »), la destination de l'argent vaut « simple correction » par défaut, seuls le Directeur et la
 * Finance voient le bouton, et le bouton disparaît une fois le délai de grâce (calculé par le serveur) dépassé.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function mixin({ role = 'Directeur', post } = {}) {
    const api = { post: post || (async () => ({})) };
    const ctx = loadScripts(['payment-cancel.js'], { preload: { auth: { role }, api } });
    const state = ctx.window.paymentCancel.mixin();
    state.afterPaymentCancelled = async () => { state.refreshed = true; };
    return state;
}

const PAYMENT = { paymentId: 'p-1', receiptNumber: 'REC-2026-0001', amount: 30000 };

test('seuls le Directeur et la Finance peuvent annuler un paiement', () => {
    assert.equal(mixin({ role: 'Directeur' }).canCancelPayment, true);
    assert.equal(mixin({ role: 'Finance' }).canCancelPayment, true);
    assert.equal(mixin({ role: 'Secretariat' }).canCancelPayment, false);
    assert.equal(mixin({ role: 'Enseignant' }).canCancelPayment, false);
});

test('la destination de l\'argent vaut « simple correction » par défaut', () => {
    const state = mixin();
    state.openCancelPayment(PAYMENT);
    assert.equal(state.cancelPaymentOutcome, 'KeptBySchool');
    assert.deepEqual(plain(state.cancelPaymentOutcomes.map((o) => o.value)), ['KeptBySchool', 'ReusedForEnrollment', 'RefundedToParent']);
});

test('le motif se compose : libellé de la liste, précision facultative, « Autre motif » exige une précision', () => {
    const state = mixin();
    state.openCancelPayment(PAYMENT);

    assert.equal(state.composeCancelPaymentReason(), '', 'aucun choix : aucun motif');

    state.cancelPaymentChoice = 'Montant saisi incorrect';
    assert.equal(state.composeCancelPaymentReason(), 'Montant saisi incorrect');
    assert.equal(state.cancelPaymentDetailRequired(), false);

    state.cancelPaymentDetail = ' 30 000 au lieu de 3 000 ';
    assert.equal(state.composeCancelPaymentReason(), 'Montant saisi incorrect — 30 000 au lieu de 3 000');

    state.cancelPaymentChoice = 'Autre motif';
    assert.equal(state.cancelPaymentDetailRequired(), true);
    assert.equal(state.composeCancelPaymentReason(), 'Autre : 30 000 au lieu de 3 000');
});

test('sans motif choisi, rien n\'est envoyé au serveur', async () => {
    let calls = 0;
    const state = mixin({ post: async () => { calls++; } });
    state.openCancelPayment(PAYMENT);

    await state.confirmCancelPayment();

    assert.equal(calls, 0);
    assert.match(state.cancelPaymentError, /motif/i);
});

test('« Autre motif » sans précision de 3 caractères est refusé côté écran', async () => {
    let calls = 0;
    const state = mixin({ post: async () => { calls++; } });
    state.openCancelPayment(PAYMENT);
    state.cancelPaymentChoice = 'Autre motif';
    state.cancelPaymentDetail = 'ab';

    await state.confirmCancelPayment();

    assert.equal(calls, 0);
    assert.match(state.cancelPaymentError, /3 caractères/);
});

test('l\'annulation envoie le motif et la destination, puis relit l\'écran', async () => {
    const sent = [];
    const state = mixin({ post: async (url, body) => { sent.push({ url, body }); return {}; } });
    state.openCancelPayment(PAYMENT);
    state.cancelPaymentChoice = 'Paiement enregistré sur le mauvais élève';
    state.cancelPaymentOutcome = 'ReusedForEnrollment';

    await state.confirmCancelPayment();
    await flush();

    assert.deepEqual(plain(sent), [{
        url: '/finance/payments/p-1/cancel',
        body: { reason: 'Paiement enregistré sur le mauvais élève', outcome: 'ReusedForEnrollment' }
    }]);
    assert.equal(state.cancelingPayment, null, 'la fenêtre se ferme');
    assert.equal(state.refreshed, true, 'l\'écran hôte relit le solde et l\'historique');
});

test('une erreur serveur (déjà annulé, délai dépassé) est affichée telle quelle et la fenêtre reste ouverte', async () => {
    const state = mixin({
        post: async () => { throw Object.assign(new Error('Le délai d\'annulation de 48 heures est dépassé.'), { code: 'PAYMENT_CANCELLATION_WINDOW_EXPIRED' }); }
    });
    state.openCancelPayment(PAYMENT);
    state.cancelPaymentChoice = 'Paiement enregistré en double';

    await state.confirmCancelPayment();

    assert.match(state.cancelPaymentError, /48 heures/);
    assert.notEqual(state.cancelingPayment, null);
    assert.equal(state.isCancelingPayment, false);
});

test('le bouton disparaît quand le délai de grâce calculé par le serveur est dépassé, ou pour un paiement déjà annulé', () => {
    const state = mixin();
    const inOneHour = new Date(Date.now() + 3600 * 1000).toISOString();
    const oneHourAgo = new Date(Date.now() - 3600 * 1000).toISOString();

    assert.equal(state.isPaymentCancellable({ status: 'Paid', cancellableUntil: inOneHour }), true);
    assert.equal(state.isPaymentCancellable({ status: 'Paid', cancellableUntil: oneHourAgo }), false);
    assert.equal(state.isPaymentCancellable({ status: 'Cancelled', cancellableUntil: inOneHour }), false);
    assert.equal(state.isPaymentCancellable({ status: 'Paid' }), true, 'sans échéance connue, le serveur tranche');
});
