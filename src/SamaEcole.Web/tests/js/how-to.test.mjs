/**
 * Bouton « Comment faire ? » (wwwroot/js/how-to.js) : il réutilise les articles du Centre d'aide.
 * Deux garanties : (1) le rapprochement page → article (chemin, sous-chemin, onglet) ; (2) chaque
 * écran important dispose réellement d'un guide dans les données d'aide, sinon le bouton ouvrirait
 * une fenêtre vide sur un écran où l'on a justement besoin d'aide.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { loadScripts, plain } from './harness.mjs';

const sections = [
    { articles: [
        { id: 'a', title: 'Élèves', href: '/eleves', procedure: ['1'], recommandations: ['r'] },
        { id: 'b', title: 'Sécurité', href: '/parametres?tab=securite' },
        { id: 'c', title: 'Années', href: '/parametres?tab=annees-scolaires' },
        { id: 'd', title: 'Panneau Paramètres', href: '/parametres' },
        { id: 'e', title: 'Aide', href: '/aide' }
    ] }
];

function howToMatch() {
    return loadScripts(['how-to.js']).window.howToMatch;
}

test('un article est rapproché de sa page, y compris pour un sous-chemin', () => {
    const match = howToMatch();
    assert.deepEqual(plain(match(sections, '/eleves', '').map((a) => a.id)), ['a']);
    assert.deepEqual(plain(match(sections, '/eleves/123', '').map((a) => a.id)), ['a']);
    assert.deepEqual(plain(match(sections, '/classes', '').map((a) => a.id)), []);
});

test("sur une page à onglets, l'article de l'onglet ouvert passe devant, puis ceux sans onglet", () => {
    const match = howToMatch();
    assert.deepEqual(plain(match(sections, '/parametres', '?tab=securite').map((a) => a.id)), ['b', 'd', 'c']);
    assert.deepEqual(plain(match(sections, '/parametres', '').map((a) => a.id)[0]), 'd');
});

test("le Centre d'aide lui-même n'a pas de guide contextuel", () => {
    assert.deepEqual(plain(howToMatch()(sections, '/aide', '').map((a) => a.id)), []);
});

test("les écrans de configuration importants ont un guide dans le Centre d'aide", () => {
    const ctx = loadScripts(['help.js', 'how-to.js']);
    const real = ctx.window.HELP_SECTIONS;
    assert.ok(Array.isArray(real) && real.length > 0, 'help.js doit publier window.HELP_SECTIONS');
    for (const page of ['/eleves', '/inscriptions', '/matieres', '/classes', '/caisse', '/enseignants', '/parametres', '/inventaire']) {
        const found = ctx.window.howToMatch(real, page, '');
        assert.ok(found.length > 0, `aucun guide pour ${page}`);
        for (const article of found) {
            assert.ok(Array.isArray(article.procedure) && article.procedure.length > 0, `guide sans étapes : ${article.id}`);
        }
    }
});

/** Tous les .cshtml sous Views/. */
function cshtmlFiles(dir) {
    return readdirSync(dir).flatMap((name) => {
        const full = join(dir, name);
        return statSync(full).isDirectory() ? cshtmlFiles(full) : full.endsWith('.cshtml') ? [full] : [];
    });
}

test("chaque <how-to-button article=\"…\"> des vues pointe vers un article qui existe dans le Centre d'aide", () => {
    const ctx = loadScripts(['help.js', 'how-to.js']);
    const real = ctx.window.HELP_SECTIONS;
    const ids = new Set(real.flatMap((section) => section.articles.map((a) => a.id)));
    const used = [];
    for (const file of cshtmlFiles(new URL('../../Views', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'))) {
        for (const m of readFileSync(file, 'utf8').matchAll(/<how-to-button\s+article="([^"]+)"/g)) used.push({ file, id: m[1] });
    }
    assert.ok(used.length >= 10, `bouton d'aide spécifique attendu sur au moins 10 écrans (trouvé ${used.length})`);
    for (const { file, id } of used) assert.ok(ids.has(id), `article inconnu « ${id} » dans ${file}`);
});
