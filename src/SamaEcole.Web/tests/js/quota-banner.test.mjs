/**
 * Bandeau de quota d'élèves du tableau de bord (wwwroot/js/quota-banner.js).
 *
 * Le serveur décide de l'état (WithinQuota / InTolerance / Exceeded) et de la tranche suggérée ; ces tests prouvent que le bandeau
 * ne s'affiche que quand il le faut, parle juste (avertissement doux dans la tolérance, blocage au-delà), ne propose la montée en
 * gamme qu'au Directeur et seulement sans demande en cours, et ouvre la modale d'abonnement avec la tranche suggérée.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const QUOTA = (overrides = {}) => ({
    currentStudentCount: 155,
    maxStudentLimit: 150,
    softQuotaLimit: 160,
    state: 'InTolerance',
    tier: 'Tier1_150',
    tierLabel: "jusqu'à 150 élèves",
    suggestedTier: 'Tier_250',
    suggestedTierLabel: "jusqu'à 250 élèves",
    overQuotaSince: '2026-10-13T08:00:00Z',
    pendingChangeRequestId: null,
    ...overrides
});

async function bootBanner({ role = 'Directeur', quota = QUOTA(), fail = false } = {}) {
    const requested = [];
    const calls = [];
    const ctx = loadScripts(['quota-banner.js', 'subscription-change.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push(endpoint);
                    if (fail) throw new Error('réseau');
                    return quota;
                },
                post: async (endpoint, body) => { requested.push({ endpoint, body: plain(body) }); return { hasChanges: true, canRequest: true, request: body }; },
                toMessage: (_e, fallback) => fallback
            }
        }
    });
    const components = ctx.initAlpine();
    const banner = components.get('quotaBanner')();
    await banner.init();
    await flush();
    return { banner, calls, requested, store: ctx.store('subscriptionChange') };
}

test('une école dans son quota ne voit aucun bandeau', async () => {
    const { banner } = await bootBanner({ quota: QUOTA({ state: 'WithinQuota', suggestedTier: null }) });

    assert.equal(banner.visible, false);
});

test("dans la tolérance : avertissement doux avec le nombre d'élèves encore admis", async () => {
    const { banner } = await bootBanner();

    assert.equal(banner.visible, true);
    assert.equal(banner.isExceeded, false);
    assert.equal(banner.remaining, 5);
    assert.match(banner.title, /dépasse votre tranche/);
    assert.match(banner.message, /155 élèves/);
    assert.match(banner.message, /encore inscrire 5 élèves/);
    assert.match(banner.message, /tolérance jusqu'à 160/);
});

test("au-delà de la tolérance : message de blocage des inscriptions et des imports", async () => {
    const { banner } = await bootBanner({ quota: QUOTA({ state: 'Exceeded', currentStudentCount: 165 }) });

    assert.equal(banner.isExceeded, true);
    assert.equal(banner.remaining, 0);
    assert.match(banner.title, /Limite d'élèves atteinte/);
    assert.match(banner.message, /bloqués/);
});

test("tolérance épuisée : le bandeau annonce que la prochaine inscription sera bloquée, sans « 0 élève »", async () => {
    const { banner } = await bootBanner({ quota: QUOTA({ currentStudentCount: 160 }) });

    assert.equal(banner.remaining, 0);
    assert.match(banner.message, /prochaine inscription sera bloquée/);
    assert.doesNotMatch(banner.message, /encore inscrire 0/);
});

test("le singulier s'accorde quand il ne reste qu'un élève", async () => {
    const { banner } = await bootBanner({ quota: QUOTA({ currentStudentCount: 159 }) });

    assert.match(banner.message, /encore inscrire 1 élève avant/);
});

test('le Directeur peut demander la tranche suggérée ; la modale reçoit l\'aperçu du serveur pour cette tranche', async () => {
    const { banner, requested, store } = await bootBanner();

    assert.equal(banner.canRequestUpgrade, true);
    assert.equal(banner.upgradeLabel, "Passer à la tranche « jusqu'à 250 élèves »");

    banner.upgrade();
    await flush();

    assert.deepEqual(requested[0], { endpoint: '/tenant-subscription/change-requests/preview', body: { tier: 'Tier_250' } });
    assert.equal(store.open, true);
});

test('la tranche Grand Groupe se demande avec son libellé propre', async () => {
    const { banner } = await bootBanner({ quota: QUOTA({ suggestedTier: 'Tier3_800', suggestedTierLabel: 'Grand Groupe (sur devis)' }) });

    assert.equal(banner.upgradeLabel, "Demander l'offre Grand Groupe");
});

test('le Secrétariat voit le bandeau mais ne peut pas demander la montée en gamme', async () => {
    const { banner, requested } = await bootBanner({ role: 'Secretariat' });

    assert.equal(banner.visible, true);
    assert.equal(banner.canRequestUpgrade, false);
    banner.upgrade();
    await flush();
    assert.equal(requested.length, 0);
});

test('une demande déjà en cours remplace le bouton par son statut', async () => {
    const { banner } = await bootBanner({ quota: QUOTA({ pendingChangeRequestId: 'req-1' }) });

    assert.equal(banner.hasPendingRequest, true);
    assert.equal(banner.canRequestUpgrade, false);
});

test("sans tranche à proposer, le bandeau informe sans bouton", async () => {
    const { banner } = await bootBanner({ quota: QUOTA({ suggestedTier: null, suggestedTierLabel: null }) });

    assert.equal(banner.visible, true);
    assert.equal(banner.canRequestUpgrade, false);
});

test("les autres rôles n'appellent même pas la route", async () => {
    const { banner, calls } = await bootBanner({ role: 'Enseignant' });

    assert.equal(calls.length, 0);
    assert.equal(banner.visible, false);
});

test("un abonnement non actif (204 vide) ou un échec réseau n'affichent rien et ne cassent pas le tableau de bord", async () => {
    assert.equal((await bootBanner({ quota: null })).banner.visible, false);
    assert.equal((await bootBanner({ fail: true })).banner.visible, false);
});
