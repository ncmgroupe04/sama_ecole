/**
 * Onboarding & Pricing SaaS côté navigateur :
 *   1. le store schoolConfig (auth.js) charge les réglages ET mémorise le profil pour l'atterrissage, sans
 *      que la seconde opération ne puisse faire échouer la première — une régression réelle : la table de
 *      correspondance était hors de portée du store, la ReferenceError était avalée par son catch, et le
 *      module Internat repassait à « masqué » alors que l'API le disait activé ;
 *   2. l'atterrissage du Directeur dépend du profil (Daara → suivi coranique) et est effacé avec la session ;
 *   3. api.js redirige sur 403 ONBOARDING_REQUIRED, sans boucle et sans réagir à un 403 ordinaire ;
 *   4. l'assistant /onboarding/select-profile (onboarding.js).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

function bootAuth({ role = 'Directeur', settings, apiError } = {}) {
    const ctx = loadScripts(['auth.js'], {
        preload: {
            api: {
                get: async (url) => {
                    if (apiError) throw apiError;
                    assert.equal(url, '/schools/current/settings');
                    return settings;
                }
            }
        }
    });

    ctx.window.auth.saveSession(
        { accessToken: fakeJwt({ sub: 'u1', schoolId: 's1', role, exp: Math.floor(Date.now() / 1000) + 900 }), expiresIn: 900 },
        'user@test.sn');
    return ctx;
}

const SETTINGS = (overrides) => ({
    isPedagogyEnabled: true, isFinanceEnabled: true, isInternatEnabled: false,
    typeEtablissement: 'Prive', workingDays: ['Monday'], profileEtablissement: 'General', ...overrides
});

// ---------------------------------------------------------------- 1. store schoolConfig

test('le store garde les modules de l\'API ET mémorise le profil Daara pour l\'atterrissage', async () => {
    const ctx = bootAuth({ settings: SETTINGS({ isInternatEnabled: true, profileEtablissement: 'DaaraInternat' }) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.internatEnabled, true, 'le module Internat doit rester activé (régression : repassait à false)');
    assert.equal(store.profileEtablissement, 'DaaraInternat');
    assert.equal(ctx.window.auth.rememberedProfile(), 'InternatDaara');
});

test('chaque profil des réglages se traduit vers le ProfileType de la souscription', async () => {
    const expected = {
        Simplifie: 'ComptabiliteRapports', ElementairePrimaire: 'Elementaire', General: 'EnseignementGeneral',
        FrancoArabe: 'FrancoArabe', DaaraInternat: 'InternatDaara'
    };

    for (const [establishment, profileType] of Object.entries(expected)) {
        const ctx = bootAuth({ settings: SETTINGS({ profileEtablissement: establishment }) });
        await ctx.store('schoolConfig').init();
        assert.equal(ctx.window.auth.rememberedProfile(), profileType, establishment);
    }
});

test('une école sans profil enregistré (null) est traitée comme Enseignement Général', async () => {
    const ctx = bootAuth({ settings: SETTINGS({ profileEtablissement: null }) });

    await ctx.store('schoolConfig').init();

    assert.equal(ctx.window.auth.rememberedProfile(), 'EnseignementGeneral');
});

test('une erreur de chargement ne mémorise aucun profil', async () => {
    const ctx = bootAuth({ apiError: new Error('réseau') });

    await ctx.store('schoolConfig').init();

    assert.equal(ctx.window.auth.rememberedProfile(), null);
});

// ---------------------------------------------------------------- 2. atterrissage

test('le Directeur d\'un Daara atterrit sur le suivi coranique, les autres profils gardent le défaut du rôle', () => {
    const ctx = bootAuth({ role: 'Directeur' });

    assert.equal(ctx.window.auth.defaultLandingForRole(), '/tableau-de-bord', 'sans profil mémorisé');

    ctx.window.auth.rememberProfile('InternatDaara');
    assert.equal(ctx.window.auth.defaultLandingForRole(), '/suivi-coranique');

    for (const profile of ['Elementaire', 'FrancoArabe', 'EnseignementGeneral', 'ComptabiliteRapports']) {
        ctx.window.auth.rememberProfile(profile);
        assert.equal(ctx.window.auth.defaultLandingForRole(), '/tableau-de-bord', profile);
    }
});

test('l\'atterrissage par profil ne concerne que le Directeur', () => {
    for (const [role, landing] of [['Secretariat', '/eleves'], ['Finance', '/tableau-de-bord']]) {
        const ctx = bootAuth({ role });
        ctx.window.auth.rememberProfile('InternatDaara');
        assert.equal(ctx.window.auth.defaultLandingForRole(), landing, role);
    }
});

test('le profil mémorisé est effacé avec la session (jamais d\'un compte à l\'autre)', () => {
    const ctx = bootAuth({ role: 'Directeur' });
    ctx.window.auth.rememberProfile('InternatDaara');

    ctx.window.auth.clearSession();

    assert.equal(ctx.window.auth.rememberedProfile(), null);
});

// ---------------------------------------------------------------- 3. api.js

async function apiAnswering(status, payload, pathname = '/eleves') {
    const ctx = loadScripts(['api.js'], {
        fetch: async () => ({ ok: false, status, json: async () => payload }),
        preload: {
            auth: { accessToken: 'jwt', isAuthenticated: () => false, isAccessTokenStale: () => false, redirectToLogin() {} }
        }
    });
    ctx.window.location.pathname = pathname;
    const assigned = [];
    ctx.window.location.assign = (url) => assigned.push(url);

    await assert.rejects(ctx.window.api.get('/students'));
    return assigned;
}

const ONBOARDING_REQUIRED = {
    code: 'ONBOARDING_REQUIRED', message: 'Choisissez…', details: { redirectTo: '/onboarding/select-profile' }, traceId: 't'
};

test('ONBOARDING_REQUIRED redirige vers la destination indiquée par le serveur', async () => {
    assert.deepEqual(await apiAnswering(403, ONBOARDING_REQUIRED), ['/onboarding/select-profile']);
});

test('ONBOARDING_REQUIRED sans destination retombe sur le chemin connu', async () => {
    assert.deepEqual(await apiAnswering(403, { ...ONBOARDING_REQUIRED, details: null }), ['/onboarding/select-profile']);
});

test('aucune redirection si l\'on est déjà sur l\'écran d\'Onboarding (pas de boucle)', async () => {
    assert.deepEqual(await apiAnswering(403, ONBOARDING_REQUIRED, '/onboarding/select-profile'), []);
});

test('un 403 ordinaire ne redirige pas', async () => {
    assert.deepEqual(await apiAnswering(403, { code: 'MODULE_DISABLED', message: 'non', details: null, traceId: 't' }), []);
});

// ---------------------------------------------------------------- 4. assistant

function bootWizard({ role = 'Directeur', subscription = { status: 'PendingOnboarding' }, getError, postError } = {}) {
    const calls = [];
    const redirects = [];
    let remembered = null;
    const ctx = loadScripts(['onboarding.js'], {
        preload: {
            auth: {
                role,
                defaultLandingForRole: () => '/suivi-coranique',
                rememberProfile: (p) => { remembered = p; },
                logout() {}
            },
            api: {
                get: async (url) => { calls.push(['GET', url]); if (getError) throw getError; return subscription; },
                post: async (url, body) => {
                    calls.push(['POST', url, body]);
                    if (postError) throw postError;
                    return { profileType: body.profile };
                }
            }
        }
    });
    ctx.window.location.assign = (u) => redirects.push(['assign', u]);
    ctx.window.location.replace = (u) => redirects.push(['replace', u]);

    return { wizard: ctx.component('onboardingWizard'), calls, redirects, remembered: () => remembered };
}

test('l\'assistant propose les 5 profils de l\'API et 3 tranches (le sur-mesure n\'est jamais proposé)', () => {
    const { wizard } = bootWizard();

    assert.deepEqual(plain(wizard.profiles.map((p) => p.id).sort()),
        ['ComptabiliteRapports', 'Elementaire', 'EnseignementGeneral', 'FrancoArabe', 'InternatDaara']);
    assert.deepEqual(plain(wizard.tiers.map((t) => t.id)), ['Tier1_150', 'Tier2_400', 'Tier3_800']);
});

test('l\'envoi exige un profil ET une tranche, puis poste les deux et redirige selon le profil', async () => {
    const { wizard, calls, redirects, remembered } = bootWizard();
    await wizard.load();
    assert.equal(wizard.state, 'ready');
    assert.equal(wizard.canSubmit, false);

    wizard.profile = 'InternatDaara';
    assert.equal(wizard.canSubmit, false);
    wizard.tier = 'Tier2_400';
    assert.equal(wizard.canSubmit, true);

    await wizard.submit();

    assert.deepEqual(plain(calls.at(-1)), ['POST', '/onboarding/select-profile', { profile: 'InternatDaara', tier: 'Tier2_400' }]);
    assert.equal(remembered(), 'InternatDaara');
    assert.deepEqual(redirects, [['assign', '/suivi-coranique']]);
});

test('un autre rôle que le Directeur voit le message d\'attente', async () => {
    const { wizard } = bootWizard({ role: 'Secretariat' });
    await wizard.load();
    assert.equal(wizard.state, 'waitDirector');
});

test('une école déjà configurée est renvoyée vers son atterrissage', async () => {
    const { wizard, redirects } = bootWizard({ subscription: { status: 'Active' } });
    await wizard.load();
    assert.deepEqual(redirects, [['replace', '/suivi-coranique']]);
});

test('une erreur de chargement propose de réessayer', async () => {
    const { wizard } = bootWizard({ getError: new Error('boom') });
    await wizard.load();
    assert.equal(wizard.state, 'loadError');
    assert.equal(wizard.error, 'boom');
});

test('une erreur d\'envoi s\'affiche et réactive le bouton', async () => {
    const { wizard } = bootWizard({ postError: Object.assign(new Error('refus'), { code: 'X' }) });
    await wizard.load();
    wizard.profile = 'FrancoArabe';
    wizard.tier = 'Tier1_150';

    await wizard.submit();

    assert.equal(wizard.error, 'refus');
    assert.equal(wizard.isSubmitting, false);
});

test('ONBOARDING_ALREADY_COMPLETED (autre onglet) redirige sans afficher d\'erreur', async () => {
    const { wizard, redirects } = bootWizard({ postError: Object.assign(new Error('déjà'), { code: 'ONBOARDING_ALREADY_COMPLETED' }) });
    await wizard.load();
    wizard.profile = 'FrancoArabe';
    wizard.tier = 'Tier1_150';

    await wizard.submit();

    assert.equal(wizard.error, null);
    assert.deepEqual(redirects, [['replace', '/suivi-coranique']]);
});

test('un clic pendant l\'envoi n\'envoie pas une seconde fois', async () => {
    const { wizard, calls } = bootWizard();
    await wizard.load();
    wizard.profile = 'Elementaire';
    wizard.tier = 'Tier1_150';

    const first = wizard.submit();
    const second = wizard.submit();
    await Promise.all([first, second]);
    await flush();

    assert.equal(calls.filter((c) => c[0] === 'POST').length, 1);
});
