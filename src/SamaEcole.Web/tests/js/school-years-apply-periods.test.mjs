/**
 * Écran Années scolaires (`schoolYearsView`, wwwroot/js/school-years.js) — « Appliquer le découpage ».
 *
 * L'action rejoue sur UNE année le découpage choisi dans Paramètres (trimestres / semestres /
 * périodes). Le serveur la refuse (422) dès qu'une note ou une appréciation de bulletin existe : l'écran
 * doit alors AFFICHER la phrase du serveur (elle dit quoi faire), pas un message générique, et ne rien
 * annoncer comme réussi.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

const OPEN_YEAR = { id: 'y-1', label: '2026-2027', startDate: '2026-10-01', endDate: '2027-06-30', isActive: true, isClosed: false };
const CLOSED_YEAR = { id: 'y-0', label: '2024-2025', startDate: '2024-10-01', endDate: '2025-06-30', isActive: false, isClosed: true };

async function schoolYearsView({ role = 'Directeur', postImpl } = {}) {
    const posts = [];
    let listCalls = 0;
    const ctx = loadScripts(['school-years.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    listCalls += 1;
                    return [OPEN_YEAR, CLOSED_YEAR];
                },
                post: async (endpoint, body) => {
                    posts.push({ endpoint, body });
                    return postImpl ? postImpl() : [];
                },
                toMessage: (err, fallback) => (err && err.message) || fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    const view = ctx.component('schoolYearsView');
    await flush();
    return { view, posts, listCalls: () => listCalls };
}

test('l\'action est proposée au Directeur sur une année non terminée seulement', async () => {
    const { view } = await schoolYearsView();

    assert.equal(view.canApplyPeriods(OPEN_YEAR), true);
    assert.equal(view.canApplyPeriods(CLOSED_YEAR), false);
});

test('un autre rôle que le Directeur ne la voit pas', async () => {
    const { view } = await schoolYearsView({ role: 'Secretariat' });

    assert.equal(view.canApplyPeriods(OPEN_YEAR), false);
});

test('confirmer appelle la route de l\'année, recharge la liste et annonce le succès', async () => {
    const { view, posts, listCalls } = await schoolYearsView();
    const before = listCalls();

    view.openApplyPeriods(OPEN_YEAR);
    assert.equal(view.yearToApplyPeriods.id, 'y-1');

    await view.submitApplyPeriods();

    assert.deepEqual(posts.map((p) => p.endpoint), ['/school-years/y-1/apply-evaluation-periods']);
    assert.equal(listCalls(), before + 1, 'la liste est rechargée');
    assert.equal(view.yearToApplyPeriods, null, 'la modale se ferme');
    assert.equal(view.showPeriodsAppliedDialog, true);
    assert.equal(view.appliedPeriodsYearLabel, '2026-2027');
});

test('un refus du serveur (notes déjà saisies) affiche SA phrase et n\'annonce aucun succès', async () => {
    const message = "Des notes sont déjà saisies sur l'année « 2026-2027 » : son découpage ne peut plus changer.";
    const { view } = await schoolYearsView({ postImpl: async () => { throw new Error(message); } });

    view.openApplyPeriods(OPEN_YEAR);
    await view.submitApplyPeriods();

    assert.equal(view.applyPeriodsError, message);
    assert.equal(view.showPeriodsAppliedDialog, false);
    assert.equal(view.yearToApplyPeriods.id, 'y-1', 'la modale reste ouverte pour lire le motif');
    assert.equal(view.isApplyingPeriods, false);
});

test('fermer la modale efface le motif du refus précédent', async () => {
    const { view } = await schoolYearsView({ postImpl: async () => { throw new Error('refus'); } });

    view.openApplyPeriods(OPEN_YEAR);
    await view.submitApplyPeriods();
    view.closeApplyPeriods();
    view.openApplyPeriods(OPEN_YEAR);

    assert.equal(view.applyPeriodsError, null);
});
