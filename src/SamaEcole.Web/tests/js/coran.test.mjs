/**
 * Écran Coran/Franco-Arabe (/coran) — spec docs/superpowers/specs/2026-09-29-coran-screen-design.md.
 *
 * Section 1 : store `schoolConfig.coranEnabled` (auth.js), sur le modèle exact d'`internatEnabled`
 * (même précaution "sûr par défaut = caché" : module réservé/inerte tant que non activé).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

/** JWT minimal — seul le payload compte, readClaims() ne vérifie jamais la signature. */
function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

/** Charge auth.js avec une session Directeur posée ; `settings` = réponse de GET /schools/current/settings. */
function bootAuth({ settings, failSettings = false } = {}) {
    const ctx = loadScripts(['auth.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') {
                        if (failSettings) throw new Error('réseau');
                        return settings ? settings() : {};
                    }
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                put: async (_endpoint, body) => body,
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            pdfPreview: { state: () => ({}) },
            location: { href: 'https://localhost/x', search: '' },
            history: { replaceState() {} }
        }
    });
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role: 'Directeur' }), expiresIn: 900 });
    return ctx;
}

test('coranEnabled suit isCoranModuleEnabled du serveur quand vrai', async () => {
    const ctx = bootAuth({ settings: () => ({ isCoranModuleEnabled: true }) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.coranEnabled, true);
});

test('coranEnabled reste false (masqué) tant que la réponse manque ou en cas d\'erreur réseau', async () => {
    const ctx = bootAuth({ failSettings: true });
    const store = ctx.store('schoolConfig');

    assert.equal(store.coranEnabled, false, 'avant la réponse');
    await store.init();
    assert.equal(store.coranEnabled, false, 'après une erreur réseau — sûr par défaut = caché');
});

test('coranEnabled reste false si le serveur omet le champ (ancienne réponse)', async () => {
    const ctx = bootAuth({ settings: () => ({}) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.coranEnabled, false);
});

test('sidebarNav.coranEnabled délègue au store partagé', async () => {
    const ctx = bootAuth({ settings: () => ({ isCoranModuleEnabled: true }) });
    await ctx.store('schoolConfig').init();

    const nav = ctx.component('sidebarNav');

    assert.equal(nav.coranEnabled, true);
    assert.deepEqual(plain({ x: nav.coranEnabled }), { x: true });
});
