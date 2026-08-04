/**
 * Détection de connectivité et badge d'état (wwwroot/js/network-guard.js).
 *
 * Le cas décisif est le portail captif / la 4G qui ne route plus : `navigator.onLine` reste à true,
 * l'interface réseau étant bien active, alors qu'aucune requête n'aboutit. Un voyant vert dans cette
 * situation est pire qu'absent — il fait croire à une secrétaire que son enregistrement est parti.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

function bootGuard({ onLine = true, probe = async () => ({ ok: true, status: 200 }) } = {}) {
    const calls = [];
    const ctx = loadScripts(['form-draft.js', 'network-guard.js'], {
        onLine,
        fetch: async (url, init) => {
            calls.push({ url, method: init?.method });
            return probe(url, init);
        },
        preload: { auth: { schoolId: 'ecole-a' } }
    });
    return { ctx, calls };
}

test('l\'état initial suit le navigateur', () => {
    assert.equal(bootGuard().ctx.window.networkGuard.state, 'online');
    assert.equal(bootGuard({ onLine: false }).ctx.window.networkGuard.state, 'offline');
});

test('la sonde confirme la liaison serveur et repasse au vert', async () => {
    const { ctx, calls } = bootGuard();
    ctx.window.networkGuard.state = 'offline';

    const states = [];
    ctx.window.networkGuard.onChange((online, state) => states.push(state));

    const reachable = await ctx.window.networkGuard.check();

    assert.equal(reachable, true);
    assert.deepEqual(states, ['checking', 'online'], 'l\'état transitoire doit être exposé');
    assert.equal(calls[0].method, 'HEAD', 'la sonde ne télécharge rien');
    assert.ok(calls[0].url.includes('_probe='), 'la sonde déjoue les caches intermédiaires');
});

test('serveur injoignable alors que navigator.onLine ment : le badge passe hors ligne', async () => {
    const { ctx } = bootGuard({ probe: async () => { throw new TypeError('Failed to fetch'); } });

    assert.equal(ctx.navigator.onLine, true, 'le navigateur se croit connecté');

    const reachable = await ctx.window.networkGuard.check();

    assert.equal(reachable, false);
    assert.equal(ctx.window.networkGuard.state, 'offline');
});

test('une réponse HTTP en erreur compte comme injoignable', async () => {
    const { ctx } = bootGuard({ probe: async () => ({ ok: false, status: 502 }) });

    assert.equal(await ctx.window.networkGuard.check(), false);
    assert.equal(ctx.window.networkGuard.state, 'offline');
});

test('marteler le bouton ne déclenche qu\'une seule sonde', async () => {
    const { ctx, calls } = bootGuard({
        probe: async () => { await flush(5); return { ok: true, status: 200 }; }
    });

    await Promise.all([
        ctx.window.networkGuard.check(),
        ctx.window.networkGuard.check(),
        ctx.window.networkGuard.check()
    ]);

    assert.equal(calls.length, 1);
});

test('une coupure annoncée par le navigateur ne déclenche aucune sonde', async () => {
    const { ctx, calls } = bootGuard();

    ctx.navigator.onLine = false;
    ctx.window.emit('offline');
    await flush();

    assert.equal(ctx.window.networkGuard.state, 'offline');
    assert.equal(calls.length, 0, 'inutile de sonder pour confirmer une coupure franche');
});

test('le retour du réseau est vérifié avant de repasser au vert', async () => {
    const { ctx, calls } = bootGuard({ onLine: false });

    ctx.navigator.onLine = true;
    ctx.window.emit('online');
    await flush();

    assert.equal(ctx.window.networkGuard.state, 'online');
    assert.equal(calls.length, 1, 'le guard vérifie plutôt que de croire navigator.onLine');
});

test('le badge annonce les saisies conservées, jamais une synchronisation', async () => {
    const { ctx } = bootGuard();
    const badge = ctx.component('connectivityBadge');

    assert.equal(badge.label(), 'Connecté');

    ctx.window.formDraft.save('caisse', { amount: 25000 });
    ctx.navigator.onLine = false;
    ctx.window.emit('offline');
    await flush();

    assert.equal(badge.label(), 'Hors ligne — 1 saisie conservée');
    assert.ok(!badge.title().toLowerCase().includes('synchronis'));
    assert.ok(badge.title().includes("rien n'est enregistré"));

    ctx.window.formDraft.save('inscription', { fullName: 'Awa Diop' });
    assert.equal(badge.label(), 'Hors ligne — 2 saisies conservées');
});

test('le toast de reconnexion survit au passage par l\'état « vérification »', async () => {
    const { ctx } = bootGuard({ onLine: false });
    const banner = ctx.component('networkGuardBanner');

    assert.equal(banner.offline, true);

    ctx.navigator.onLine = true;
    ctx.window.emit('online');
    await flush();

    assert.equal(banner.offline, false);
    assert.equal(banner.showReconnectedToast, true);
});
