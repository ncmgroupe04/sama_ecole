/**
 * Paramètres › Modules › « Cycles gérés » (settings.js : toggleManagedCycle).
 *
 *   - l'état affiché ne change QU'APRÈS la réponse du serveur : une case ne se décoche pas « pour voir » ;
 *   - un cycle qui contient des classes est refusé (409 CYCLE_HAS_CLASSROOMS) : la case reste cochée et l'écran
 *     affiche le message du serveur avec un lien vers les classes ;
 *   - au moins un cycle reste coché ;
 *   - le réglage ne voyage JAMAIS avec le PUT général (saveConfig), qui ne le connaît pas.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const ALL = ['Maternelle', 'Primaire', 'College', 'Lycee'];

function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

async function settingsView({ role = 'Directeur', managed = ALL, putResult, putError } = {}) {
    const calls = [];
    const ctx = loadScripts(['auth.js', 'settings.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') {
                        return { gradingScale: '20', workingDays: ['Monday'], managedCycles: managed, profileEtablissement: 'General' };
                    }
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                put: async (endpoint, body) => {
                    calls.push({ endpoint, body: plain(body) });
                    if (putError) throw putError;
                    return putResult ? putResult(body) : { managedCycles: body.cycles };
                },
                toMessage: (e, fallback) => (e && e.message) || fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/parametres', search: '' },
            history: { replaceState() {} }
        }
    });
    // auth.js (chargé pour le store schoolConfig) pose le vrai window.auth : le rôle vient d'une session factice.
    ctx.window.auth.saveSession(
        { accessToken: fakeJwt({ sub: 'u1', schoolId: 's1', role, exp: Math.floor(Date.now() / 1000) + 900 }), expiresIn: 900 },
        'user@test.sn');
    const view = ctx.component('settingsView');
    await flush();
    return { view, calls, ctx };
}

test('le chargement lit les cycles gérés des réglages', async () => {
    const { view } = await settingsView({ managed: ['Maternelle', 'Primaire'] });

    assert.deepEqual(plain(view.managedCycles), ['Maternelle', 'Primaire']);
    assert.equal(view.isCycleChecked('Primaire'), true);
    assert.equal(view.isCycleChecked('Lycee'), false);
});

test('sans cycles dans la réponse (API ancienne), tous les cycles sont cochés', async () => {
    const { view } = await settingsView({ managed: undefined });

    assert.deepEqual(plain(view.managedCycles), ALL);
});

test('décocher un cycle envoie la liste restante, dans l\'ordre canonique, à l\'endpoint dédié', async () => {
    const { view, calls } = await settingsView();

    await view.toggleManagedCycle('College');

    assert.deepEqual(calls, [{ endpoint: '/schools/current/settings/managed-cycles', body: { cycles: ['Maternelle', 'Primaire', 'Lycee'] } }]);
    assert.deepEqual(plain(view.managedCycles), ['Maternelle', 'Primaire', 'Lycee']);
    assert.equal(view.cyclesSaved, true);
    assert.equal(view.cyclesError, null);
});

test('cocher un cycle le remet à sa place canonique, pas à la fin', async () => {
    const { view, calls } = await settingsView({ managed: ['Maternelle', 'Lycee'] });

    await view.toggleManagedCycle('Primaire');

    assert.deepEqual(calls[0].body.cycles, ['Maternelle', 'Primaire', 'Lycee']);
});

test('le refus 409 laisse la case cochée, affiche le message du serveur et propose les classes', async () => {
    const refusal = Object.assign(new Error('Le cycle Collège compte 4 classes. Supprimez-les ou déplacez-les avant de le désactiver.'),
        { code: 'CYCLE_HAS_CLASSROOMS', status: 409 });
    const { view } = await settingsView({ putError: refusal });

    await view.toggleManagedCycle('College');

    assert.equal(view.isCycleChecked('College'), true, 'le cycle reste géré : rien n\'a été écrit');
    assert.match(view.cyclesError, /Collège compte 4 classes/);
    assert.equal(view.cyclesBlocked, true);
    assert.equal(view.cyclesSaved, false);
    assert.equal(view.cyclesSaving, false, 'la case est de nouveau utilisable');
});

test('une autre erreur affiche un message et ne propose pas le lien vers les classes', async () => {
    const { view } = await settingsView({ putError: Object.assign(new Error('Service indisponible.'), { status: 503 }) });

    await view.toggleManagedCycle('Lycee');

    assert.equal(view.cyclesError, 'Service indisponible.');
    assert.equal(view.cyclesBlocked, false);
    assert.equal(view.isCycleChecked('Lycee'), true);
});

test('le dernier cycle coché ne peut pas être décoché', async () => {
    const { view, calls } = await settingsView({ managed: ['Primaire'] });

    assert.equal(view.canToggleCycle('Primaire'), false);
    await view.toggleManagedCycle('Primaire');

    assert.deepEqual(calls, [], 'aucune requête partie');
    assert.deepEqual(plain(view.managedCycles), ['Primaire']);
    assert.equal(view.canToggleCycle('College'), true, 'on peut toujours en ajouter un');
});

test('seul le Directeur peut modifier les cycles', async () => {
    const { view, calls } = await settingsView({ role: 'Secretariat' });

    assert.equal(view.canToggleCycle('College'), false);
    await view.toggleManagedCycle('College');

    assert.deepEqual(calls, []);
});

test('pendant un envoi, aucune case n\'est utilisable (pas de double clic)', async () => {
    let release;
    const { view, calls } = await settingsView({ putResult: (body) => new Promise((resolve) => { release = () => resolve({ managedCycles: body.cycles }); }) });

    const first = view.toggleManagedCycle('College');
    await flush();
    assert.equal(view.cyclesSaving, true);
    assert.equal(view.canToggleCycle('Lycee'), false);

    await view.toggleManagedCycle('Lycee');
    assert.equal(calls.length, 1, 'le second clic est ignoré');

    release();
    await first;
    assert.equal(view.cyclesSaving, false);
});

test('après un enregistrement, le store partagé est rechargé pour que les formulaires se mettent à jour', async () => {
    const { view, ctx } = await settingsView();
    ctx.window.Alpine = ctx.sandbox.Alpine; // en production, Alpine est aussi exposé sur window
    const store = ctx.store('schoolConfig');
    let reloaded = 0;
    store.init = async () => { reloaded += 1; };

    await view.toggleManagedCycle('Lycee');

    assert.equal(reloaded, 1);
});

test('le PUT général ne modifie pas les cycles : il renvoie la valeur du serveur telle quelle (et le serveur l\'ignore)', async () => {
    const { view, calls } = await settingsView({ managed: ['Maternelle', 'Primaire'] });
    view.managedCycles = ['Primaire']; // un état local divergent ne doit JAMAIS partir avec le PUT général
    view.config.autoLogoutMinutes = 20;

    await view.saveConfig();

    const general = calls.find((c) => c.endpoint === '/schools/current/settings');
    assert.ok(general, 'le PUT général est parti');
    // Le corps étale la relecture serveur (voir saveConfig) : la valeur renvoyée est celle du SERVEUR, pas l'état local.
    assert.deepEqual(general.body.managedCycles, ['Maternelle', 'Primaire']);
    assert.equal('cycles' in general.body, false);
});
