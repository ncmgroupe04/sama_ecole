/**
 * Guichet rapide de la caisse (`caisseView`, wwwroot/js/caisse.js) — la pop-up « Encaissement des
 * frais dus » et sa sélection par cases à cocher.
 *
 * Règle métier verrouillée ici : la sélection est CONTIGUË. L'imputation en base suit l'ordre des
 * échéances (InstallmentScheduleCalculator, oldest-first), donc seul un PRÉFIXE des frais dus peut
 * passer proprement à « Réglé » — décocher une ligne doit décocher toutes les suivantes, et le
 * total à encaisser suit.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function caisseView() {
    const ctx = loadScripts(['caisse.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            networkGuard: { newIdempotencyKey: () => 'key' },
            formatFCFA: (n) => String(n)
        }
    });
    const components = ctx.initAlpine();
    // Sans init() : il appelle $watch (absent hors Alpine). On teste la logique de sélection, pure.
    return components.get('caisseView')();
}

/** Échéancier type : inscription + uniforme + 9 mensualités, rien de versé. */
function balanceWith3DueLines() {
    return {
        enrollmentId: 'enr-1',
        installments: [
            { id: 'i1', designation: 'Inscription', remainingDue: 35000, isInitialScope: true, feeCategoryId: 'c-insc' },
            { id: 'i2', designation: 'Uniforme', remainingDue: 12000, isInitialScope: true, feeCategoryId: 'c-unif' },
            { id: 'i3', designation: 'Mensualité (Mois 1)', remainingDue: 22000, isInitialScope: true, feeCategoryId: 'c-scol' },
            { id: 'i4', designation: 'Mensualité (Mois 2)', remainingDue: 22000, isInitialScope: false, feeCategoryId: 'c-scol' }
        ]
    };
}

test('quickPayRows ne retient que les échéances de l\'engagement initial encore dues', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    assert.deepEqual(v.quickPayRows().map((r) => r.designation),
        ['Inscription', 'Uniforme', 'Mensualité (Mois 1)']);
});

test('openQuickPay coche tout par défaut et pré-remplit le montant avec le total', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    v.openQuickPay();
    assert.deepEqual(v.quickPay.checked, [true, true, true]);
    assert.equal(v.quickPayTotal(), 69000);
    assert.equal(v.quickPay.amount, 69000);
    assert.equal(v.quickPay.open, true);
});

test('décocher une ligne du milieu décoche aussi toutes les suivantes, et le total suit', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    v.openQuickPay();

    // L'utilisateur décoche « Uniforme » (index 1).
    v.quickPay.checked[1] = false;
    v.onQuickRowToggle(1);

    assert.deepEqual(v.quickPay.checked, [true, false, false], 'Mois 1 doit suivre Uniforme');
    assert.equal(v.quickPayTotal(), 35000, 'seul l\'inscription reste');
    assert.equal(v.quickPay.amount, 35000);
});

test('recocher une ligne recoche aussi toutes celles qui précèdent', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();
    v.openQuickPay();
    v.quickPay.checked = [false, false, false];

    // L'utilisateur coche « Mensualité (Mois 1) » (index 2).
    v.quickPay.checked[2] = true;
    v.onQuickRowToggle(2);

    assert.deepEqual(v.quickPay.checked, [true, true, true]);
    assert.equal(v.quickPayTotal(), 69000);
});

test('rouvrir le guichet après [Annuler] repart sur toutes les cases cochées', () => {
    const v = caisseView();
    v.balance = balanceWith3DueLines();

    v.openQuickPay();
    v.quickPay.checked = [true, false, false]; // l'utilisateur avait restreint la sélection
    v.closeQuickPay();
    assert.equal(v.quickPay.open, false);
    // L'élève reste sélectionné : le solde est toujours là.
    assert.ok(v.balance);

    v.openQuickPay(); // clic sur « ⚡ Encaisser les frais dus »
    assert.equal(v.quickPay.open, true);
    assert.deepEqual(v.quickPay.checked, [true, true, true]);
    assert.equal(v.quickPay.amount, 69000);
});

test('quickPayNote extrait « Mois 1 » d\'une désignation de mensualité, null sinon', () => {
    const v = caisseView();
    assert.equal(v.quickPayNote('Mensualité (Mois 1)'), 'Mois 1');
    assert.equal(v.quickPayNote('Inscription'), null);
});

test('dueNowTotal privilégie la valeur serveur, et retombe sur les échéances isInitialScope', () => {
    const v = caisseView();
    v.balance = { ...balanceWith3DueLines(), dueNowTotal: 69000 };
    assert.equal(v.dueNowTotal, 69000);

    const noServerValue = balanceWith3DueLines();
    v.balance = noServerValue; // pas de champ dueNowTotal
    assert.equal(v.dueNowTotal, 69000);
});
