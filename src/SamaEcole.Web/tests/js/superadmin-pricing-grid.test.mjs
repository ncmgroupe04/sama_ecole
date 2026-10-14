/**
 * Console Super Admin › Tarification : grille par tranche d'effectif (wwwroot/js/superadmin-pricing.js).
 * Le serveur reste seul juge des montants ; ces tests prouvent que l'écran n'envoie que les quatre montants saisis
 * (jamais de plafond ni de tranche modifiée), range correctement les libellés de tranche et rend l'erreur du
 * serveur sous son champ sans fermer la modale.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function row(tier, maxStudents, softLimit, monthlyXof, annualXof, extra = {}) {
    return {
        price: {
            tier, maxStudents, softLimit, monthlyXof, annualXof,
            boardingSurchargeMonthlyXof: 0, boardingSurchargeAnnualXof: 0, ...extra
        },
        updatedAt: null,
        updatedBy: null
    };
}

const GRID = [
    row('Tier_100', 100, 110, 5000, 50000),
    row('Tier1_150', 150, 160, 10000, 100000),
    row('Tier_250', 250, 260, 15000, 150000),
    row('Tier2_400', 400, 420, 25000, 250000)
];

function boot(options = {}) {
    const calls = [];
    const ctx = loadScripts(['superadmin-pricing.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/admin/pricing-grid') {
                        if (options.gridError) throw options.gridError;
                        return structuredClone(GRID);
                    }
                    return [];
                },
                put: async (endpoint, body) => {
                    calls.push({ method: 'PUT', endpoint, body: plain(body) });
                    if (options.putError) throw options.putError;
                    const tier = endpoint.split('/').pop();
                    return { ...row(tier, 100, 110, body.monthlyXof, body.annualXof, body), updatedAt: '2026-10-13T08:00:00Z', updatedBy: 'sa' };
                },
                toMessage: (err, fallback) => fallback,
                toFieldErrors: (err, fallback) => (err && err.details
                    ? Object.fromEntries(Object.entries(err.details).map(([k, v]) => [k.toLowerCase(), v[0]]))
                    : { global: fallback })
            }
        }
    });
    return { page: ctx.initAlpine().get('superAdminPricing')(), calls };
}

test('loads the grid and derives the quote threshold from the last priced tier', async () => {
    const { page } = boot();

    await page.load();

    assert.equal(page.grid.length, 4);
    assert.equal(page.quoteAboveStudents, 400);
    assert.equal(page.gridError, null);
});

test('labels each tier by its own bounds', async () => {
    const { page } = boot();
    await page.load();

    assert.deepEqual(page.grid.map((r) => page.tierLabel(r.price)), [
        "Jusqu'à 100 élèves", '101 à 150 élèves', '151 à 250 élèves', '251 à 400 élèves'
    ]);
});

test('shows how many months the yearly price pays', () => {
    const { page } = boot();

    assert.equal(page.paidMonthsLabel({ monthlyXof: 5000, annualXof: 50000 }), '= 10 mois payés');
    assert.equal(page.paidMonthsLabel({ monthlyXof: 5000, annualXof: 55000 }), '= 11 mois payés');
    assert.equal(page.paidMonthsLabel({ monthlyXof: null, annualXof: 50000 }), '');
});

test('shows "Aucune" when no boarding surcharge is configured', () => {
    const { page } = boot();

    assert.equal(page.surchargeLabel({ boardingSurchargeMonthlyXof: 0, boardingSurchargeAnnualXof: 0 }), 'Aucune');
});

test('saves only the four edited amounts and replaces the row', async () => {
    const { page, calls } = boot();
    await page.load();

    page.openPriceEdit(page.grid[0]);
    page.priceDraft.monthlyXof = 6000;
    page.priceDraft.annualXof = 60000;
    await page.submitPriceEdit();

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/admin/pricing-grid/Tier_100');
    assert.deepEqual(Object.keys(put.body).sort(), [
        'annualXof', 'boardingSurchargeAnnualXof', 'boardingSurchargeMonthlyXof', 'monthlyXof'
    ]);
    assert.equal(page.grid[0].price.monthlyXof, 6000);
    assert.equal(page.priceEdit, null, 'la modale se ferme après enregistrement');
    assert.equal(page.isSavingPrice, false);
});

test('renders a server validation error under its field and keeps the modal open', async () => {
    const error = Object.assign(new Error('422'), { status: 422, details: { AnnualXof: ['Le tarif annuel ne peut pas dépasser 12 mensualités.'] } });
    const { page } = boot({ putError: error });
    await page.load();

    page.openPriceEdit(page.grid[1]);
    page.priceDraft.annualXof = 999999;
    await page.submitPriceEdit();
    await flush();

    assert.equal(page.priceErrors.annualxof, 'Le tarif annuel ne peut pas dépasser 12 mensualités.');
    assert.notEqual(page.priceEdit, null);
    assert.equal(page.grid[1].price.annualXof, 100000, 'la grille affichée ne change pas');
});

test('reports a load failure without crashing the promo codes list', async () => {
    const { page } = boot({ gridError: new Error('boom') });

    await page.load();

    assert.equal(page.gridError, 'Erreur lors du chargement de la grille tarifaire.');
    assert.equal(page.grid.length, 0);
});
