/**
 * Store schoolConfig (auth.js) — état des modules pour la garde de page (lot G, tâche 1) :
 *   - `coranEnabled` suit `isCoranModuleEnabled`, comme `internatEnabled` suit `isInternatEnabled` ;
 *   - `loadFailed` distingue « module désactivé » de « on n'a pas pu le savoir » : sans lui, une coupure réseau
 *     (fréquente) éjecterait à tort un Directeur de Daara de son module principal ;
 *   - `retry()` relance le chargement après un échec.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

/** `respond(callNumber)` renvoie les réglages ou lève une erreur, selon le scénario. */
function boot(respond, { role = 'Directeur' } = {}) {
    let calls = 0;
    const ctx = loadScripts(['auth.js'], {
        preload: {
            api: {
                get: async (url) => {
                    assert.equal(url, '/schools/current/settings');
                    calls += 1;
                    return respond(calls);
                }
            }
        }
    });

    ctx.window.auth.saveSession(
        { accessToken: fakeJwt({ sub: 'u1', schoolId: 's1', role, exp: Math.floor(Date.now() / 1000) + 900 }), expiresIn: 900 },
        'user@test.sn');
    return { ctx, store: ctx.store('schoolConfig'), calls: () => calls };
}

const SETTINGS = (overrides) => ({
    isPedagogyEnabled: true, isFinanceEnabled: true, isInternatEnabled: false, isCoranModuleEnabled: false,
    typeEtablissement: 'Prive', workingDays: ['Monday'], profileEtablissement: 'General', ...overrides
});

test('schoolConfig modules : avant tout chargement, rien n\'est activé et rien n\'a échoué', () => {
    const { store } = boot(() => SETTINGS());

    assert.equal(store.internatEnabled, false);
    assert.equal(store.coranEnabled, false);
    assert.equal(store.loadFailed, false);
});

test('schoolConfig modules : les deux interrupteurs sont lus indépendamment', async () => {
    for (const [internat, coran] of [[true, false], [false, true], [true, true], [false, false]]) {
        const { store } = boot(() => SETTINGS({ isInternatEnabled: internat, isCoranModuleEnabled: coran }));

        await store.init();

        assert.equal(store.internatEnabled, internat, `internat=${internat}, coran=${coran}`);
        assert.equal(store.coranEnabled, coran, `internat=${internat}, coran=${coran}`);
        assert.equal(store.loadFailed, false);
    }
});

test('schoolConfig modules : un champ absent ou non booléen ne vaut jamais « activé »', async () => {
    const { store } = boot(() => SETTINGS({ isInternatEnabled: undefined, isCoranModuleEnabled: 'true' }));

    await store.init();

    assert.equal(store.internatEnabled, false);
    assert.equal(store.coranEnabled, false);
});

test('schoolConfig modules : une erreur réseau désactive par sécurité mais SIGNALE l\'échec', async () => {
    const { store } = boot(() => { throw new Error('network'); });

    await store.init();

    assert.equal(store.internatEnabled, false, 'sûr par défaut : rien n\'est affiché');
    assert.equal(store.coranEnabled, false);
    assert.equal(store.loadFailed, true, 'la garde de page ne doit PAS prendre ce « false » pour « module désactivé »');
    assert.equal(store.loaded, true);
    assert.equal(store.profileEtablissement, undefined, 'le profil reste « inconnu », jamais forcé à null');
});

test('schoolConfig modules : un second init() ne relance pas de requête', async () => {
    const { store, calls } = boot(() => SETTINGS({ isInternatEnabled: true }));

    await Promise.all([store.init(), store.init()]);
    await store.init();

    assert.equal(calls(), 1);
});

test('schoolConfig modules : retry() relance le chargement et efface l\'échec au succès', async () => {
    const { store, calls } = boot((n) => {
        if (n === 1) throw new Error('network');
        return SETTINGS({ isInternatEnabled: true, isCoranModuleEnabled: true });
    });

    await store.init();
    assert.equal(store.loadFailed, true);

    await store.retry();

    assert.equal(calls(), 2);
    assert.equal(store.loadFailed, false);
    assert.equal(store.internatEnabled, true);
    assert.equal(store.coranEnabled, true);
});

test('schoolConfig modules : retry() qui échoue encore laisse l\'échec signalé', async () => {
    const { store, calls } = boot(() => { throw new Error('network'); });

    await store.init();
    await store.retry();

    assert.equal(calls(), 2);
    assert.equal(store.loadFailed, true);
    assert.equal(store.internatEnabled, false);
});

test('schoolConfig modules : le Super Admin n\'a pas d\'école, donc pas de requête ni d\'échec', async () => {
    const { store, calls } = boot(() => SETTINGS({ isInternatEnabled: true }), { role: 'SuperAdmin' });

    await store.init();

    assert.equal(calls(), 0);
    assert.equal(store.loadFailed, false);
    assert.equal(store.loaded, true);
});
