/**
 * Garde de page par module (lot G, tâche 3) — module-route-guard.js.
 *
 * Règle d'or : la redirection est de l'ERGONOMIE, jamais une barrière d'accès (la vraie protection est l'API :
 * [RequireModule] → 403 MODULE_DISABLED, rôles, RLS). Donc :
 *   - module activé → on affiche ;
 *   - module désactivé → on redirige vers l'atterrissage du rôle, avec un message ;
 *   - ÉCHEC de lecture des réglages → on ne redirige JAMAIS (une coupure réseau éjecterait à tort un Directeur de Daara de
 *     son module principal) : bandeau « Réessayer » ;
 *   - jamais de boucle de redirection (atterrissage par profil lui-même gardé).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

const guard = () => loadScripts(['module-route-guard.js']).window.moduleRouteGuard;

// ---------------------------------------------------------------- decide (pure)

const store = (overrides = {}) => ({
    internatEnabled: false, coranEnabled: false, pedagogyEnabled: true, financeEnabled: true, loadFailed: false, ...overrides
});

test('decide : sans module requis, la page s\'affiche', () => {
    assert.equal(guard().decide({ requiredModule: null, store: store() }), 'show');
    assert.equal(guard().decide({ requiredModule: '', store: store() }), 'show');
});

test('decide : module activé → afficher', () => {
    assert.equal(guard().decide({ requiredModule: 'Internat', store: store({ internatEnabled: true }) }), 'show');
    assert.equal(guard().decide({ requiredModule: 'Coran', store: store({ coranEnabled: true }) }), 'show');
});

test('decide : module désactivé et réglages bien lus → rediriger', () => {
    assert.equal(guard().decide({ requiredModule: 'Internat', store: store() }), 'redirect');
    assert.equal(guard().decide({ requiredModule: 'Coran', store: store({ internatEnabled: true }) }), 'redirect',
        'Coran et Internat sont deux interrupteurs distincts');
});

test('decide : ÉCHEC de lecture → non vérifié, jamais une redirection', () => {
    assert.equal(guard().decide({ requiredModule: 'Internat', store: store({ loadFailed: true }) }), 'unverified');
    assert.equal(guard().decide({ requiredModule: 'Coran', store: store({ loadFailed: true }) }), 'unverified');
});

test('decide : un module inconnu ne bloque jamais', () => {
    assert.equal(guard().decide({ requiredModule: 'Inconnu', store: store() }), 'show');
});

// ---------------------------------------------------------------- apply (dépendances injectées)

function makeDeps({
    required = 'Internat', internat = true, coran = false, loadFailed = false, role = 'Directeur', authenticated = true,
    landing = '/suivi-coranique', neutral = '/tableau-de-bord', pathname = '/internat', stored = {}, now = 1_000_000,
    retryOutcome = null
} = {}) {
    const calls = { replaced: [], toasts: [], attrs: {}, banner: null, inits: 0, retries: 0 };
    const memory = new Map(Object.entries(stored));

    const theStore = {
        internatEnabled: internat, coranEnabled: coran, pedagogyEnabled: true, financeEnabled: true, loadFailed,
        init: async () => { calls.inits += 1; },
        retry: async () => { calls.retries += 1; Object.assign(theStore, retryOutcome || {}); }
    };

    const element = (tag) => {
        const el = { tag, className: '', textContent: '', children: [], listeners: {}, removed: false };
        el.appendChild = (c) => { el.children.push(c); return c; };
        el.addEventListener = (type, cb) => { el.listeners[type] = cb; };
        el.remove = () => { el.removed = true; };
        return el;
    };
    const main = { children: [], prepend(el) { this.children.unshift(el); calls.banner = el; } };

    const doc = {
        querySelector(selector) {
            if (selector.includes('required-module')) return required ? { getAttribute: () => required } : null;
            if (selector === 'main') return main;
            return null;
        },
        createElement: element,
        documentElement: { setAttribute: (k, v) => { calls.attrs[k] = v; } }
    };

    return {
        calls, main, store: theStore, memory,
        deps: {
            doc,
            win: { location: { pathname, replace: (url) => calls.replaced.push(url) } },
            auth: {
                role,
                isAuthenticated: () => authenticated,
                defaultLandingForRole: () => landing,
                neutralLandingForRole: () => neutral
            },
            store: theStore,
            toast: { error: (m) => calls.toasts.push(m) },
            storage: {
                getItem: (k) => (memory.has(k) ? memory.get(k) : null),
                setItem: (k, v) => memory.set(k, String(v)),
                removeItem: (k) => memory.delete(k)
            },
            now: () => now
        }
    };
}

test('apply : sans balise required-module, rien ne change (la page s\'affiche)', async () => {
    const t = makeDeps({ required: null });

    assert.equal(await guard().apply(t.deps), 'show');
    assert.equal(t.calls.inits, 0, 'aucune lecture des réglages pour une page sans module');
    assert.deepEqual(t.calls.replaced, []);
});

test('apply : module activé → la page s\'affiche, le contenu est révélé', async () => {
    const t = makeDeps({ internat: true });

    assert.equal(await guard().apply(t.deps), 'show');
    assert.equal(t.calls.attrs['data-module-guard'], 'done');
    assert.deepEqual(t.calls.replaced, []);
});

test('apply : module désactivé → redirection vers l\'atterrissage du rôle + message mémorisé pour la page suivante', async () => {
    const t = makeDeps({ internat: false, landing: '/tableau-de-bord' });

    assert.equal(await guard().apply(t.deps), 'redirect');
    assert.deepEqual(t.calls.replaced, ['/tableau-de-bord']);
    assert.match(t.memory.get('unikol.moduleGuard.notice'), /pas activé/);
    assert.notEqual(t.calls.attrs['data-module-guard'], 'done', 'le contenu reste masqué pendant la redirection');
});

test('apply : jamais de redirection vers la page courante, ni de boucle sur un atterrissage lui-même gardé', async () => {
    // Directeur de profil Daara dont le module Internat a été désactivé : l'atterrissage par profil est /suivi-coranique.
    const first = makeDeps({ internat: false, landing: '/suivi-coranique', pathname: '/internat', now: 1_000_000 });
    await guard().apply(first.deps);
    assert.deepEqual(first.calls.replaced, ['/suivi-coranique']);

    // Arrivé sur /suivi-coranique (gardée) quelques secondes plus tard : on ne renvoie pas au même endroit.
    const second = makeDeps({
        internat: false, landing: '/suivi-coranique', neutral: '/tableau-de-bord', pathname: '/suivi-coranique',
        stored: Object.fromEntries(first.memory), now: 1_000_000 + 3_000
    });
    await guard().apply(second.deps);
    assert.deepEqual(second.calls.replaced, ['/tableau-de-bord'], 'repli neutre : plus de boucle');
});

test('apply : l\'atterrissage identique à la page courante est remplacé par le repli neutre', async () => {
    const t = makeDeps({ internat: false, landing: '/internat', pathname: '/internat', neutral: '/eleves' });

    await guard().apply(t.deps);

    assert.deepEqual(t.calls.replaced, ['/eleves']);
});

test('apply : ÉCHEC de lecture → aucune redirection, bandeau « Réessayer »', async () => {
    const t = makeDeps({ internat: false, loadFailed: true });

    assert.equal(await guard().apply(t.deps), 'unverified');
    assert.deepEqual(t.calls.replaced, [], 'une coupure réseau n\'éjecte jamais l\'utilisateur de son module');
    assert.equal(t.calls.attrs['data-module-guard'], 'done', 'le contenu est affiché (l\'API restera le juge)');
    assert.ok(t.calls.banner, 'le bandeau est inséré en tête de <main>');
    assert.match(t.calls.banner.children.map((c) => c.textContent).join(' '), /Impossible de vérifier/);
});

test('apply : « Réessayer » relance le chargement, retire le bandeau et applique la décision', async () => {
    const t = makeDeps({ internat: false, loadFailed: true, retryOutcome: { loadFailed: false, internatEnabled: true } });
    await guard().apply(t.deps);
    const retryButton = t.calls.banner.children.find((c) => c.listeners.click);

    await retryButton.listeners.click();

    assert.equal(t.calls.retries, 1);
    assert.equal(t.calls.banner.removed, true);
    assert.deepEqual(t.calls.replaced, [], 'le module est maintenant activé : on reste');
});

test('apply : « Réessayer » qui aboutit à « désactivé » redirige', async () => {
    const t = makeDeps({ internat: false, loadFailed: true, landing: '/eleves', retryOutcome: { loadFailed: false, internatEnabled: false } });
    await guard().apply(t.deps);

    await t.calls.banner.children.find((c) => c.listeners.click).listeners.click();

    assert.deepEqual(t.calls.replaced, ['/eleves']);
});

test('apply : le Super Admin (sans école) et l\'utilisateur non connecté ne sont jamais redirigés par la garde', async () => {
    for (const overrides of [{ role: 'SuperAdmin' }, { authenticated: false }]) {
        const t = makeDeps({ internat: false, ...overrides });

        assert.equal(await guard().apply(t.deps), 'show');
        assert.deepEqual(t.calls.replaced, []);
        assert.equal(t.calls.inits, 0);
    }
});

test('showPendingNotice : le message mémorisé est affiché UNE fois, sur la page suivante', () => {
    const t = makeDeps({ stored: { 'unikol.moduleGuard.notice': 'Ce module n\'est pas activé pour votre établissement.' } });

    guard().showPendingNotice(t.deps);
    guard().showPendingNotice(t.deps);

    assert.deepEqual(t.calls.toasts, ['Ce module n\'est pas activé pour votre établissement.']);
});

// ---------------------------------------------------------------- auth.js : repli neutre

test('auth.neutralLandingForRole ignore le profil mémorisé (c\'est le point de repli de la garde)', () => {
    const ctx = loadScripts(['auth.js']);
    const jwt = (claims) => `h.${Buffer.from(JSON.stringify(claims)).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')}.s`;
    ctx.window.auth.saveSession(
        { accessToken: jwt({ sub: 'u', schoolId: 's', role: 'Directeur', exp: Math.floor(Date.now() / 1000) + 900 }), expiresIn: 900 }, 'a@b.sn');
    ctx.window.auth.rememberEstablishmentProfile('DaaraInternat');

    assert.equal(ctx.window.auth.defaultLandingForRole(), '/suivi-coranique', 'atterrissage par profil (écran gardé par Internat)');
    assert.equal(ctx.window.auth.neutralLandingForRole(), '/tableau-de-bord', 'repli neutre : jamais un écran gardé par un module');
});
