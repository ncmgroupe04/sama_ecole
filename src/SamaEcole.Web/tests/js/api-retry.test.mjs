/**
 * Reprise automatique des défaillances réseau passagères (wwwroot/js/api.js).
 *
 * Le test qui compte vraiment ici est « une écriture n'est JAMAIS rejouée » : c'est la garantie qui
 * empêche un encaissement d'être enregistré deux fois quand la 4G lâche entre la requête et la
 * réponse. Les autres vérifient que la reprise fait bien son travail sur les lectures.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

/** Doublure de fetch renvoyant les résultats programmés, l'un après l'autre. */
function scriptedFetch(steps) {
    const calls = [];
    const fetchStub = async (url, init) => {
        calls.push({ url, method: init?.method });
        const step = steps[Math.min(calls.length - 1, steps.length - 1)];

        if (step instanceof Error) throw step;
        return { ok: step.status < 400, status: step.status, json: async () => step.body ?? {} };
    };
    return { fetchStub, calls };
}

function networkFailure() {
    // Ce que lève réellement fetch sur une coupure : api.js le traduit en code NETWORK_OFFLINE.
    return new TypeError('Failed to fetch');
}

function bootApi(fetchStub, { onLine = true } = {}) {
    const ctx = loadScripts(['api.js'], {
        onLine,
        fetch: fetchStub,
        preload: {
            auth: {
                accessToken: 'jwt-de-test',
                isAuthenticated: () => false,
                isAccessTokenStale: () => false,
                redirectToLogin() {}
            }
        }
    });

    // Reprises instantanées : on teste la politique, pas la durée des paliers.
    ctx.window.api.retryBaseDelayMs = 1;
    return ctx;
}

test('un GET coupé par le réseau est rejoué et finit par aboutir', async () => {
    const { fetchStub, calls } = scriptedFetch([networkFailure(), { status: 200, body: { total: 3 } }]);
    const ctx = bootApi(fetchStub);

    const result = await ctx.window.api.get('/students');

    assert.deepEqual(result, { total: 3 });
    assert.equal(calls.length, 2, 'la lecture doit être retentée une fois');
});

test("un POST coupé par le réseau n'est JAMAIS rejoué (double encaissement)", async () => {
    const { fetchStub, calls } = scriptedFetch([networkFailure()]);
    const ctx = bootApi(fetchStub);

    await assert.rejects(
        () => ctx.window.api.post('/payments', { amount: 25000 }),
        (error) => error.code === 'NETWORK_OFFLINE'
    );

    assert.equal(calls.length, 1, 'une écriture ne doit partir qu\'une seule fois');
});

test("PUT, PATCH et DELETE ne sont pas rejoués non plus", async () => {
    for (const call of [
        (api) => api.put('/students/1', {}),
        (api) => api.patch('/students/1', {}),
        (api) => api.delete('/students/1')
    ]) {
        const { fetchStub, calls } = scriptedFetch([networkFailure()]);
        const ctx = bootApi(fetchStub);

        await assert.rejects(() => call(ctx.window.api));
        assert.equal(calls.length, 1);
    }
});

test('un 503 passager sur une lecture est rejoué', async () => {
    const { fetchStub, calls } = scriptedFetch([{ status: 503 }, { status: 200, body: { ok: true } }]);
    const ctx = bootApi(fetchStub);

    const result = await ctx.window.api.get('/dashboard');

    assert.deepEqual(result, { ok: true });
    assert.equal(calls.length, 2);
});

test("un 503 persistant finit par remonter l'erreur après 3 tentatives", async () => {
    const { fetchStub, calls } = scriptedFetch([{ status: 503, body: { message: 'Service indisponible' } }]);
    const ctx = bootApi(fetchStub);

    await assert.rejects(() => ctx.window.api.get('/dashboard'));
    assert.equal(calls.length, 3, 'retryMaxAttempts = 3');
});

test("un 404 n'est pas rejoué : le serveur a répondu, il a dit non", async () => {
    const { fetchStub, calls } = scriptedFetch([{ status: 404, body: { message: 'Introuvable' } }]);
    const ctx = bootApi(fetchStub);

    await assert.rejects(() => ctx.window.api.get('/students/inconnu'));
    assert.equal(calls.length, 1);
});

test('poste franchement déconnecté : échec immédiat, sans brûler de tentatives', async () => {
    const { fetchStub, calls } = scriptedFetch([{ status: 200 }]);
    const ctx = bootApi(fetchStub, { onLine: false });

    await assert.rejects(
        () => ctx.window.api.get('/students'),
        (error) => error.code === 'NETWORK_OFFLINE' && error.status === 0
    );

    assert.equal(calls.length, 0, 'aucune requête ne doit même être tentée');
});

test('le réseau tombe pendant les reprises : on arrête de réessayer', async () => {
    const { fetchStub, calls } = scriptedFetch([networkFailure()]);
    const ctx = bootApi(fetchStub);

    // Première tentative en ligne, puis le navigateur signale la coupure franche.
    const original = ctx.window.api.delay.bind(ctx.window.api);
    ctx.window.api.delay = async (ms) => {
        ctx.navigator.onLine = false;
        return original(ms);
    };

    await assert.rejects(() => ctx.window.api.get('/students'));
    assert.equal(calls.length, 1, 'la coupure confirmée arrête les reprises avant le 2e envoi');
});
