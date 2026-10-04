/**
 * Décaissements : distinction visuelle de l'écriture d'origine et de sa contre-écriture (wwwroot/js/fees.js).
 *
 * Un décaissement est une écriture comptable : il n'est jamais supprimé, « annuler » ajoute une ligne aux montants
 * opposés qui référence l'original (API : reversalOfId, isReversed). L'écran doit donc (1) ne proposer l'annulation
 * que sur une écriture normale — une double annulation ou l'annulation d'une annulation sont refusées en 409 par
 * l'API —, et (2) montrer clairement quelle ligne annule laquelle.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function bootFees() {
    const ctx = loadScripts(['fees.js'], {
        preload: {
            auth: { role: 'Directeur', accessToken: 'jwt', isAuthenticated: () => false, isAccessTokenStale: () => false, redirectToLogin() {} },
            toast: { success() {}, error() {} }
        }
    });
    return ctx.initAlpine().get('feesView')();
}

const original = { id: 'o1', beneficiary: 'Papeterie Sow', date: '2026-10-01', amount: 10000, reason: 'Craies', isReversed: true, reversalOfId: null };
const reversal = { id: 'r1', beneficiary: 'Papeterie Sow', date: '2026-10-04', amount: -10000, reason: 'Annulation — Craies', isReversed: false, reversalOfId: 'o1' };
const normal = { id: 'n1', beneficiary: 'Électricité', date: '2026-10-02', amount: 25000, reason: 'Facture', isReversed: false, reversalOfId: null };

test("trois états : écriture normale, original annulé, contre-écriture", () => {
    const fees = bootFees();

    assert.equal(fees.disbursementState(normal), 'active');
    assert.equal(fees.disbursementState(original), 'reversed');
    assert.equal(fees.disbursementState(reversal), 'reversal');
});

test("seule une écriture normale propose l'annulation (pas de double annulation ni d'annulation d'annulation)", () => {
    const fees = bootFees();

    assert.equal(fees.canReverseDisbursement(normal), true);
    assert.equal(fees.canReverseDisbursement(original), false, 'déjà annulé → 409 DISBURSEMENT_ALREADY_REVERSED côté API');
    assert.equal(fees.canReverseDisbursement(reversal), false, 'une annulation ne s\'annule pas → 409 DISBURSEMENT_IS_REVERSAL');
});

test("une contre-écriture ne tient pas pour un original, même sans champ isReversed", () => {
    const fees = bootFees();

    assert.equal(fees.disbursementState({ id: 'x', reversalOfId: 'o1' }), 'reversal');
    assert.equal(fees.disbursementState({ id: 'y' }), 'active', 'réponse d\'un ancien serveur sans les nouveaux champs');
});

test("chaque état a un style de ligne distinct", () => {
    const fees = bootFees();
    const classes = [normal, original, reversal].map((d) => fees.disbursementRowClass(d));

    assert.equal(new Set(classes).size, 3);
    assert.match(fees.disbursementRowClass(reversal), /rose/);
});

test("la contre-écriture renvoie à l'écriture d'origine quand elle est dans la liste", () => {
    const fees = bootFees();
    fees.disbursements = [reversal, original, normal];

    const note = fees.reversalNote(reversal);

    assert.match(note, /Annule le décaissement du/);
    assert.match(note, /Papeterie Sow/);
    assert.match(note, /01\/10\/2026/);
});

test("sans l'original dans la liste (filtre de dates), la note reste explicite", () => {
    const fees = bootFees();
    fees.disbursements = [reversal];

    assert.equal(fees.reversalNote(reversal), 'Annule un décaissement antérieur.');
});
