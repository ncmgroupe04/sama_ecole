/**
 * Écran Super Admin › Demandes d'inscription : le libellé de l'offre (wwwroot/js/admin-registration-requests.js).
 * Une demande par tranche d'effectif montre ce que le Super Admin approuve — profil, tranche, internat, cycles
 * cochés — et une demande historique garde son libellé « cycles · taille ».
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function view() {
    const ctx = loadScripts(['admin-registration-requests.js'], {
        preload: { auth: { role: 'SuperAdmin' }, api: {} }
    });
    return ctx.initAlpine().get('registrationRequestsView')();
}

test('describes a tiered offer with profile, tier, boarding and checked cycles', () => {
    const label = view().offerLabel({
        ownership: 'Private', cycleProfile: 'Bicycle', sizeTier: null,
        profile: 'FrancoArabe', tier: 'Tier1_150', hasBoarding: true, cycles: 'Primaire,College'
    });

    assert.equal(label, "Privé · Franco-Arabe · jusqu'à 150 élèves · Internat · Élémentaire, Moyen");
});

test('omits boarding and cycles when the school has neither', () => {
    const label = view().offerLabel({
        ownership: 'Private', cycleProfile: 'Primaire', sizeTier: null,
        profile: 'DaaraMemorisation', tier: 'Tier_100', hasBoarding: false, cycles: null
    });

    assert.equal(label, "Privé · Daara — Mémorisation · jusqu'à 100 élèves");
});

test('keeps the historical label for a request without a tier', () => {
    const page = view();

    assert.equal(
        page.offerLabel({ ownership: 'Private', cycleProfile: 'Bicycle', sizeTier: 'Medium', profile: null, tier: null }),
        'Privé · Bicycle · 400 à 800 élèves');
    assert.equal(
        page.offerLabel({ ownership: 'Public', cycleProfile: 'College', sizeTier: null, profile: null, tier: null }),
        'Public · Collège (CEM) · sur devis');
});
