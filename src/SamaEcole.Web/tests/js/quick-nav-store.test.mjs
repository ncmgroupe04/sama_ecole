/**
 * Barre de navigation rapide (_QuickNav.cshtml) — partie CLIENT uniquement : le store partagé
 * `Alpine.store('schoolConfig', …)`, la commodité `window.auth.canView(...)` et le composant
 * `quickNavMenu()` (ouverture du dropdown mobile « Saut rapide »), tous dans wwwroot/js/auth.js.
 *
 * La liste des modules, leurs gardes de rôle/formule et l'état « actif » sont rendus côté serveur
 * (Razor, `_QuickNav.cshtml`) exactement comme la sidebar — pas de logique JS à tester pour ça,
 * même principe que le reste de la sidebar aujourd'hui.
 *
 * Ce fichier verrouille surtout la RAISON D'ÊTRE du store : avant lui, `sidebarNav()` faisait son
 * propre GET /schools/current/settings à chaque chargement de page ; une barre de navigation
 * rapide ajoutée naïvement aurait fait un 4e appel redondant. Le store mutualise ce fetch, et
 * `sidebarNav()` doit continuer à se comporter EXACTEMENT comme avant (getters qui délèguent).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

/** JWT minimal — seul le payload compte, readClaims() ne vérifie jamais la signature. */
function fakeJwt(claims) {
    const json = JSON.stringify(claims);
    const b64url = Buffer.from(json, 'utf8')
        .toString('base64')
        .replace(/\+/g, '-')
        .replace(/\//g, '_')
        .replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

/** Charge auth.js avec une session déjà posée pour le rôle donné, `api.get` piloté par le test. */
function boot(role, { apiGet } = {}) {
    const ctx = loadScripts(['auth.js'], {
        preload: {
            api: { get: apiGet || (async () => ({})) }
        }
    });
    if (role) {
        ctx.window.auth.saveSession({ accessToken: fakeJwt({ role }), expiresIn: 900 });
    }
    return ctx;
}

test('schoolConfig.init() ne part au réseau qu’une seule fois, même appelé deux fois', async () => {
    let calls = 0;
    const ctx = boot('Directeur', {
        apiGet: async () => { calls++; return { isPedagogyEnabled: true, isFinanceEnabled: true, isInternatEnabled: false, typeEtablissement: 'Prive' }; }
    });
    const store = ctx.store('schoolConfig');

    await Promise.all([store.init(), store.init()]);

    assert.equal(calls, 1, 'un second init() ne doit pas redéclencher un appel réseau');
    assert.equal(store.loaded, true);
});

test('schoolConfig.init() mappe correctement une réponse réussie', async () => {
    const ctx = boot('Directeur', {
        apiGet: async () => ({ isPedagogyEnabled: false, isFinanceEnabled: true, isInternatEnabled: true, typeEtablissement: 'Public' })
    });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.isPublicSchool, true);
    assert.equal(store.pedagogyEnabled, false);
    assert.equal(store.financeEnabled, true);
    assert.equal(store.internatEnabled, true);
});

test('schoolConfig.init() retombe sur les valeurs par défaut sûres en cas d’échec réseau', async () => {
    const ctx = boot('Directeur', { apiGet: async () => { throw new Error('offline'); } });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.pedagogyEnabled, true, 'socle métier : sûr par défaut = visible');
    assert.equal(store.financeEnabled, true, 'socle métier : sûr par défaut = visible');
    assert.equal(store.internatEnabled, false, 'module réservé/inerte : sûr par défaut = masqué');
    assert.equal(store.loaded, true);
});

test('schoolConfig.init() ne tente aucun appel réseau pour un Super Admin', async () => {
    let calls = 0;
    const ctx = boot('SuperAdmin', { apiGet: async () => { calls++; return {}; } });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(calls, 0);
    assert.equal(store.loaded, true, 'loaded doit tout de même finir vrai, sans quoi un lecteur du store resterait bloqué en attente');
});

test('sidebarNav() reflète le store schoolConfig après refactor — non-régression du comportement observable', async () => {
    const ctx = boot('Directeur', {
        apiGet: async () => ({ isPedagogyEnabled: false, isFinanceEnabled: true, isInternatEnabled: true, typeEtablissement: 'Public' })
    });
    const nav = ctx.component('sidebarNav'); // component() appelle déjà init()
    await flush();

    assert.equal(nav.isPublicSchool, true);
    assert.equal(nav.pedagogyEnabled, false);
    assert.equal(nav.financeEnabled, true);
    assert.equal(nav.internatEnabled, true);
    assert.equal(nav.canView(['Directeur']), true);
    assert.equal(nav.canView(['Enseignant']), false);
});

test('window.auth.canView(...) fonctionne depuis n’importe quel sous-arbre, sans passer par sidebarNav()', () => {
    const ctx = boot('Surveillant');

    assert.equal(ctx.window.auth.canView(['Directeur', 'Surveillant']), true);
    assert.equal(ctx.window.auth.canView(['Directeur', 'Finance']), false);
});

test('quickNavMenu() bascule isOpen — seule logique cliente du dropdown « Saut rapide »', () => {
    const ctx = boot('Directeur');
    const menu = ctx.component('quickNavMenu');

    assert.equal(menu.isOpen, false);
    menu.isOpen = true;
    assert.equal(menu.isOpen, true);
});
