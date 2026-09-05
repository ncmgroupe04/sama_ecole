/**
 * Console Super Admin — données dérivées CÔTÉ CLIENT des trois graphiques ajoutés à Dashboard.cshtml,
 * Billing.cshtml et Schools.cshtml (répartition du MRR par formule, échéancier à 90 jours, courbe de
 * croissance cumulative). Seule la donnée est testée ici (getters purs) — jamais Chart.js lui-même,
 * même principe que harness.mjs pour les composants Alpine (« on teste leur logique, pas le moteur de
 * réactivité »). Les trois initializeXxxChart() gardent d'ailleurs un simple
 * `if (!this.$refs.xxx || typeof Chart === 'undefined') return;` — inertes ici, sans DOM ni Chart.js.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

// -------------------------------------------------------------------------------------------------
// mrrByPlan — même calcul dans superadmin-dashboard.js et superadmin-billing.js (Active uniquement,
// Yearly ramené à un équivalent mensuel / 12).
// -------------------------------------------------------------------------------------------------

const SUBSCRIPTIONS_FOR_MRR = [
    { plan: 'Primaire', status: 'Active', lastPaymentAmountXof: 15000, lastPaymentBillingPeriod: 'Monthly' },
    // Suspendu : ne doit PAS compter, même avec un dernier paiement confirmé (même règle que le MRR
    // serveur — AddPlatformFinancialKpis).
    { plan: 'Primaire', status: 'Suspended', lastPaymentAmountXof: 15000, lastPaymentBillingPeriod: 'Monthly' },
    // Yearly : ramené à un équivalent mensuel (300 000 / 12 = 25 000).
    { plan: 'Standard', status: 'Active', lastPaymentAmountXof: 300000, lastPaymentBillingPeriod: 'Yearly' },
    // Aucun paiement confirmé : ne doit rien ajouter.
    { plan: 'Premium', status: 'Active', lastPaymentAmountXof: null, lastPaymentBillingPeriod: 'Monthly' },
    { plan: 'Premium', status: 'Active', lastPaymentAmountXof: 50000, lastPaymentBillingPeriod: 'Monthly' }
];

test('superadmin-billing.js :: mrrByPlan somme le MRR par formule, Yearly ramené au mois, Suspendu exclu', () => {
    const ctx = loadScripts(['superadmin-billing.js']);
    const billing = ctx.component('superAdminBilling');
    billing.subscriptions = SUBSCRIPTIONS_FOR_MRR;

    assert.deepEqual(plain(billing.mrrByPlan), { Primaire: 15000, Standard: 25000, Premium: 50000 });
});

test('superadmin-dashboard.js :: mrrByPlan applique la même règle que Billing', () => {
    const ctx = loadScripts(['superadmin-dashboard.js']);
    const dashboard = ctx.component('superAdminDashboard');
    dashboard.subscriptions = SUBSCRIPTIONS_FOR_MRR;

    assert.deepEqual(plain(dashboard.mrrByPlan), { Primaire: 15000, Standard: 25000, Premium: 50000 });
});

// -------------------------------------------------------------------------------------------------
// dueSchedule (superadmin-billing.js) — échéancier à 90 jours, par tranche de 7 jours.
// -------------------------------------------------------------------------------------------------

function isoDateInDays(days) {
    const d = new Date();
    d.setHours(0, 0, 0, 0);
    d.setDate(d.getDate() + days);
    return d.toISOString().slice(0, 10); // DateOnly (expiresAt) : "yyyy-MM-dd", sans heure.
}

test('superadmin-billing.js :: dueSchedule range les échéances par semaine et ignore le passé et le hors-horizon', () => {
    const ctx = loadScripts(['superadmin-billing.js']);
    const billing = ctx.component('superAdminBilling');
    billing.subscriptions = [
        { expiresAt: isoDateInDays(3) },   // jour 3 -> semaine 0 (« S+1 »)
        { expiresAt: isoDateInDays(10) },  // jour 10 -> semaine 1 (« S+2 »)
        { expiresAt: isoDateInDays(-5) },  // déjà échu -> ignoré
        { expiresAt: isoDateInDays(200) }, // hors horizon (> 90 jours) -> ignoré
        { expiresAt: null }                // pas d'échéance -> ignoré
    ];

    const weeks = billing.dueSchedule;

    assert.equal(weeks.length, 13);
    assert.equal(weeks[0].label, 'S+1');
    assert.equal(weeks[0].count, 1);
    assert.equal(weeks[1].label, 'S+2');
    assert.equal(weeks[1].count, 1);
    assert.equal(weeks.reduce((sum, w) => sum + w.count, 0), 2, 'seules les deux échéances dans l\'horizon comptent');
});

// -------------------------------------------------------------------------------------------------
// cumulativeGrowth (superadmin-schools.js) — courbe CUMULATIVE, pas un histogramme par mois : un
// établissement créé une fois compte dans TOUS les mois qui suivent sa création, jusqu'au dernier.
// -------------------------------------------------------------------------------------------------

function isoDateMonthsAgo(months) {
    const now = new Date();
    // Jour 15 : loin des bornes de mois, aucune ambiguïté avec les arrondis de fin de mois (28-31).
    return new Date(now.getFullYear(), now.getMonth() - months, 15).toISOString();
}

test('superadmin-schools.js :: cumulativeGrowth cumule les créations mois après mois sur 12 mois', () => {
    const ctx = loadScripts(['superadmin-schools.js']);
    const schools = ctx.component('superAdminSchools');
    schools.schools = [
        { createdAt: isoDateMonthsAgo(15) }, // avant la fenêtre : compte dans les 12 mois
        { createdAt: isoDateMonthsAgo(5) },  // rejoint le cumul à partir de son propre mois
        { createdAt: isoDateMonthsAgo(0) }   // créé ce mois-ci : seul le dernier point le compte
    ];

    const months = schools.cumulativeGrowth;

    assert.equal(months.length, 12);
    // Mois 12→8 avant aujourd'hui (indices 0 à 5) : seul l'établissement le plus ancien est déjà là.
    for (let i = 0; i <= 5; i++) {
        assert.equal(months[i].count, 1, `indice ${i} : seul l'établissement de 15 mois doit déjà compter`);
    }
    // Indices 6 à 10 : le second établissement (5 mois) a rejoint le cumul.
    for (let i = 6; i <= 10; i++) {
        assert.equal(months[i].count, 2, `indice ${i} : les deux établissements les plus anciens comptent`);
    }
    // Dernier point (mois courant) : les trois comptent.
    assert.equal(months[11].count, 3, 'le mois courant doit refléter le total actuel');

    // Une courbe cumulative ne peut jamais redescendre.
    for (let i = 1; i < months.length; i++) {
        assert.ok(months[i].count >= months[i - 1].count, `le cumul ne doit jamais diminuer (indice ${i})`);
    }
});

// -------------------------------------------------------------------------------------------------
// newSchoolsByMonth (superadmin-dashboard.js) — PAR mois (pas cumulatif), contrairement à Schools.
// -------------------------------------------------------------------------------------------------

test('superadmin-dashboard.js :: newSchoolsByMonth compte les créations PAR mois, pas en cumul', () => {
    const ctx = loadScripts(['superadmin-dashboard.js']);
    const dashboard = ctx.component('superAdminDashboard');
    dashboard.schools = [
        { createdAt: isoDateMonthsAgo(15) }, // hors fenêtre des 12 derniers mois -> n'apparaît nulle part
        { createdAt: isoDateMonthsAgo(5) },
        { createdAt: isoDateMonthsAgo(5) },  // deux établissements le même mois
        { createdAt: isoDateMonthsAgo(0) }
    ];

    const months = dashboard.newSchoolsByMonth;

    assert.equal(months.length, 12);
    assert.equal(months.reduce((sum, m) => sum + m.count, 0), 3, 'l\'établissement de 15 mois est hors fenêtre');
    assert.equal(months[11].count, 1, 'le mois courant ne contient que l\'établissement créé ce mois-ci');
    assert.equal(months[6].count, 2, 'le mois « il y a 5 mois » (indice 11-5) contient les deux créations groupées');
});
