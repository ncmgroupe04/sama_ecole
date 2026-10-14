/**
 * Audit secondaire du filtrage par profil (13/10/2026) — tout est du CONFORT d'affichage, la garde réelle reste l'API :
 *   - Paramètres : l'onglet « Intégration étatique (SIMEN) » suit le module Pédagogie ; le bloc « Cycles gérés » disparaît en
 *     Daara — Mémorisation ; un lien ?tab=integration-etatique sur une école sans Pédagogie retombe sur « Profil » ;
 *   - Tableau de bord : « Taux de présence » et « Vie Scolaire & Planning » suivent le module Pédagogie ;
 *   - Enseignants : l'onglet « Emploi du Temps » suit le module Pédagogie.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { loadScripts, flush } from './harness.mjs';

function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

async function settingsView({ pedagogy = true, profile = 'General', search = '' } = {}) {
    const ctx = loadScripts(['auth.js', 'settings.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') {
                        return { gradingScale: '20', workingDays: ['Monday'], isPedagogyEnabled: pedagogy, profileEtablissement: profile };
                    }
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                toMessage: (e, fallback) => (e && e.message) || fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: `https://localhost/parametres${search}`, search },
            history: { replaceState() {} }
        }
    });
    ctx.window.auth.saveSession(
        { accessToken: fakeJwt({ sub: 'u1', schoolId: 's1', role: 'Directeur', exp: Math.floor(Date.now() / 1000) + 900 }), expiresIn: 900 },
        'user@test.sn');
    const view = ctx.component('settingsView');
    await flush();
    return view;
}

test('Paramètres : l\'onglet SIMEN suit le module Pédagogie', async () => {
    assert.equal((await settingsView({ pedagogy: true })).canViewStateIntegration, true);
    assert.equal((await settingsView({ pedagogy: false })).canViewStateIntegration, false);
});

test('Paramètres : un lien direct vers l\'onglet SIMEN retombe sur « Profil » quand la Pédagogie est coupée', async () => {
    const hidden = await settingsView({ pedagogy: false, search: '?tab=integration-etatique' });
    assert.equal(hidden.tab, 'profil');

    const shown = await settingsView({ pedagogy: true, search: '?tab=integration-etatique' });
    assert.equal(shown.tab, 'integration-etatique');
});

test('Paramètres : « Cycles gérés » est masqué en Daara — Mémorisation seulement', async () => {
    assert.equal((await settingsView({ profile: 'DaaraMemorisation' })).canViewManagedCycles, false);
    for (const profile of ['General', 'FrancoArabe', 'DaaraInternat', 'ElementairePrimaire', 'Simplifie', null]) {
        assert.equal((await settingsView({ profile })).canViewManagedCycles, true, `profil ${profile}`);
    }
});

function viewSource(relative) {
    return readFileSync(fileURLToPath(new URL(`../../Views/${relative}`, import.meta.url)), 'utf8');
}

test('Paramètres : l\'onglet ET le panneau SIMEN, et le bloc Cycles gérés, lisent leur drapeau', () => {
    const view = viewSource('Settings/Index.cshtml');

    assert.match(view.match(/<button[^>]*goToTab\('integration-etatique'\)[^>]*>/)[0], /canViewStateIntegration/);
    assert.match(view.match(/<div x-show="[^"]*tab === 'integration-etatique'"[^>]*>/)[0], /canViewStateIntegration/);
    assert.match(view.match(/<div[^>]*x-show="canViewManagedCycles"[^>]*>\s*<h3[^>]*>Cycles gérés/)[0], /canViewManagedCycles/);
});

test('Tableau de bord : les deux éléments de pédagogie lisent pedagogyEnabled', () => {
    const view = viewSource('Dashboard/Index.cshtml');

    const presence = view.match(/<div[^>]*x-show="([^"]*)">\s*<stat-card label="Taux de présence/);
    assert.ok(presence, 'la carte « Taux de présence » doit exister');
    assert.match(presence[1], /schoolConfig\.pedagogyEnabled/);

    const planning = view.match(/<div class="card[^>]*x-show="([^"]*)">\s*<div[^>]*>\s*<h2[^>]*>Vie Scolaire/);
    assert.ok(planning, 'le bloc « Vie Scolaire & Planning » doit exister');
    assert.match(planning[1], /schoolConfig\.pedagogyEnabled/);
});

test('Enseignants : l\'onglet Emploi du Temps lit pedagogyEnabled', () => {
    const tab = viewSource('Teachers/Index.cshtml').match(/<button[^>]*activeTab = 'schedule'[^>]*>/);
    assert.ok(tab, 'l\'onglet Emploi du Temps doit exister');
    assert.match(tab[0], /schoolConfig\.pedagogyEnabled/);
});
