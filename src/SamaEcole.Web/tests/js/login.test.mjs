/**
 * Formulaire de connexion (wwwroot/js/auth.js) — validation client + blocage progressif anti-force-brute.
 *
 * Ne teste PAS le chemin de connexion réussie : `window.auth.login()` réussi appelle ensuite
 * `window.location.assign(...)`, que le bac à sable (harness.mjs) ne fournit pas — ce n'est pas
 * nécessaire ici, seule la validation/le blocage réseau sont sous test.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function boot(fetchStub) {
    return loadScripts(['api.js', 'auth.js'], fetchStub ? { fetch: fetchStub } : {});
}

test('validate() rejette un formulaire vide, un champ à la fois', () => {
    const ctx = boot();
    const form = ctx.component('loginForm');

    assert.equal(form.validate(), false);
    assert.ok(form.errors.email, 'errors.email devrait être renseigné');
    assert.ok(form.errors.password, 'errors.password devrait être renseigné');
});

test('un e-mail mal formé est rejeté (EMAIL_REGEX)', () => {
    const ctx = boot();
    const form = ctx.component('loginForm');
    form.email = 'pas-un-email';
    form.password = 'peu-importe';

    assert.equal(form.validate(), false);
    assert.match(form.errors.email, /invalide/);
});

test('touchEmail() nettoie les espaces superflus puis revalide', () => {
    const ctx = boot();
    const form = ctx.component('loginForm');
    form.email = '  directeur@monecole.sn  ';
    form.password = 'peu-importe';

    form.touchEmail();

    assert.equal(form.email, 'directeur@monecole.sn');
    assert.equal(form.errors.email, undefined);
});

test('submit() ne part pas au réseau quand la validation échoue (régression : throw avant envoi)', async () => {
    const calls = [];
    const ctx = boot(async (url) => { calls.push(url); return { ok: true, status: 200, json: async () => ({}) }; });
    const form = ctx.component('loginForm');
    form.email = 'pas-un-email';
    form.password = 'secret';

    await form.submit();

    assert.equal(calls.length, 0, 'aucun appel réseau ne doit partir avec un e-mail invalide');
});

test('un 401 sans détail par champ retombe dans le bandeau global, jamais sur un champ précis', async () => {
    // InvalidCredentialsException n'a pas de `details` (voir ExceptionHandlingMiddleware) : distinguer
    // email/mot de passe ici rouvrirait l'énumération de comptes que le serveur évite déjà.
    const fetchStub = async () => ({
        ok: false,
        status: 401,
        json: async () => ({ message: 'Identifiants invalides.', code: 'INVALID_CREDENTIALS', details: null })
    });
    const ctx = boot(fetchStub);
    const form = ctx.component('loginForm');
    form.email = 'directeur@monecole.sn';
    form.password = 'mauvais-mot-de-passe';

    await form.submit();

    assert.equal(form.errors.email, undefined);
    assert.equal(form.errors.password, undefined);
    assert.match(form.error, /Identifiants invalides/);
});

test('un 429 (compte verrouillé) affiche le message global et vide le mot de passe', () => {
    // Blocage progressif anti-force-brute (docs/Volume_7_Security.md §2) : 429, retryAfterSeconds par
    // champ `details.retryAfterSeconds` — mais jamais sous errors.email/errors.password.
    const fetchStub = async () => ({
        ok: false,
        status: 429,
        json: async () => ({
            message: 'Compte temporairement verrouillé suite à plusieurs échecs de connexion. Réessayez plus tard.',
            code: 'ACCOUNT_LOCKED',
            details: { retryAfterSeconds: 60 }
        })
    });
    const ctx = boot(fetchStub);
    const form = ctx.component('loginForm');
    form.email = 'directeur@monecole.sn';
    form.password = 'mauvais-mot-de-passe';

    return form.submit().then(() => {
        assert.equal(form.errors.email, undefined);
        assert.equal(form.errors.password, undefined);
        assert.match(form.error, /verrouillé/);
        assert.equal(form.password, '');
    });
});
