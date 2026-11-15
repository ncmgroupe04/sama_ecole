/**
 * « Exporter mes données » (`schoolDataExport`, wwwroot/js/school-data-export.js) : Directeur seul, mot de passe exigé,
 * erreurs lisibles (422 sous le champ, 429, 403) et enregistrement du fichier reçu.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function makeView({ role = 'Directeur', response } = {}) {
    const saved = [];
    const requests = [];
    const ctx = loadScripts(['school-data-export.js'], {
        preload: {
            auth: { role, accessToken: 'jeton', isAuthenticated: () => true, isAccessTokenStale: () => false },
            api: { refreshOrRedirect: async () => {} }
        },
        fetch: async (url, options) => { requests.push({ url, options }); return response; }
    });
    const view = ctx.component('schoolDataExport');
    view.save = (blob, name) => saved.push({ blob, name });
    view.requests = requests;
    view.saved = saved;
    return view;
}

const fakeResponse = (status, headers = {}) => ({
    ok: status >= 200 && status < 300,
    status,
    headers: { get: (name) => headers[name] ?? null },
    blob: async () => ({ size: 10 })
});

test('seul le Directeur voit la section', () => {
    assert.equal(makeView({ role: 'Directeur' }).canExport, true);
    assert.equal(makeView({ role: 'Secretariat' }).canExport, false);
    assert.equal(makeView({ role: 'Finance' }).canExport, false);
});

test('sans mot de passe, rien n\'est envoyé et l\'erreur s\'affiche sous le champ', async () => {
    const view = makeView({ response: fakeResponse(200) });
    await view.startExport();

    assert.equal(view.requests.length, 0);
    assert.match(view.passwordError, /mot de passe/i);
});

test('un export réussi poste le mot de passe en corps (jamais dans l\'adresse), enregistre le fichier et vide le champ', async () => {
    const view = makeView({ response: fakeResponse(200, { 'Content-Disposition': 'attachment; filename=export-ecole-2026-11-15.zip' }) });
    view.password = 'Secret!2026';
    await view.startExport();

    assert.equal(view.requests[0].url, '/api/v1/exports/school-data');
    assert.equal(view.requests[0].options.method, 'POST');
    assert.equal(JSON.parse(view.requests[0].options.body).password, 'Secret!2026');
    assert.equal(view.requests[0].url.includes('Secret'), false);
    assert.equal(view.saved[0].name, 'export-ecole-2026-11-15.zip');
    assert.equal(view.password, '');
    assert.match(view.notice, /Export terminé/);
    assert.equal(view.isExporting, false);
});

test('422 : mot de passe incorrect sous le champ, aucun fichier, le mot de passe n\'est pas effacé', async () => {
    const view = makeView({ response: fakeResponse(422) });
    view.password = 'faux';
    await view.startExport();

    assert.equal(view.passwordError, 'Mot de passe incorrect.');
    assert.equal(view.saved.length, 0);
    assert.equal(view.password, 'faux');
});

test('429 et 403 donnent un message dédié', async () => {
    const limited = makeView({ response: fakeResponse(429) });
    limited.password = 'x';
    await limited.startExport();
    assert.match(limited.error, /3 par heure/);

    const forbidden = makeView({ response: fakeResponse(403) });
    forbidden.password = 'x';
    await forbidden.startExport();
    assert.match(forbidden.error, /réservé au Directeur/);
});

test('une coupure réseau est signalée sans rien enregistrer', async () => {
    const view = makeView({ response: fakeResponse(200) });
    view.password = 'x';
    // fetch qui échoue : on remplace par un rejet
    const ctx = loadScripts(['school-data-export.js'], {
        preload: { auth: { role: 'Directeur', accessToken: 't', isAuthenticated: () => true, isAccessTokenStale: () => false }, api: { refreshOrRedirect: async () => {} } },
        fetch: async () => { throw new TypeError('network'); }
    });
    const offline = ctx.component('schoolDataExport');
    offline.password = 'x';
    await offline.startExport();

    assert.match(offline.error, /Connexion interrompue/);
    assert.equal(offline.isExporting, false);
});

test('sans en-tête Content-Disposition, le fichier reçoit un nom daté', async () => {
    const view = makeView({ response: fakeResponse(200) });
    view.password = 'x';
    await view.startExport();

    assert.match(view.saved[0].name, /^export-donnees-\d{4}-\d{2}-\d{2}\.zip$/);
});
