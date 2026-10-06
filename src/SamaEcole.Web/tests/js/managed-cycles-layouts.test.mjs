/**
 * Garde de chargement : auth.js déclare le store schoolConfig, qui lit window.managedCycles (managed-cycles.js) dès
 * l'initialisation d'Alpine. Une mise en page qui charge auth.js SANS managed-cycles.js AVANT lui lève une
 * « Cannot read properties of undefined (reading 'ALL') » sur toutes ses pages (connexion, Onboarding, console
 * Super Admin, pages publiques) — erreur vue en recette navigateur, que les tests du store ne peuvent pas voir car
 * leur bac à sable précharge toujours cette aide (harness.mjs).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const VIEWS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'Views');

function cshtmlFiles(dir) {
    return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) return cshtmlFiles(full);
        return entry.name.endsWith('.cshtml') ? [full] : [];
    });
}

/** Vraies balises <script src=…> (pas les mentions dans un commentaire Razor), dans l'ordre du fichier. */
function scriptSources(file) {
    return [...readFileSync(file, 'utf8').matchAll(/<script\b[^>]*\bsrc="([^"]+)"/g)].map((m) => m[1]);
}

const loadingAuth = cshtmlFiles(VIEWS).filter((file) => scriptSources(file).some((src) => src.includes('js/auth.js')));

test('au moins les quatre mises en page connues chargent auth.js (le test ne vérifie pas le vide)', () => {
    const names = loadingAuth.map((f) => path.basename(f)).sort();

    for (const layout of ['_AuthLayout.cshtml', '_Layout.cshtml', '_LayoutPublic.cshtml', '_SuperAdminLayout.cshtml']) {
        assert.ok(names.includes(layout), `${layout} devrait charger auth.js`);
    }
});

for (const file of loadingAuth) {
    test(`${path.basename(file)} charge managed-cycles.js AVANT auth.js`, () => {
        const sources = scriptSources(file);
        const helper = sources.findIndex((src) => src.includes('js/managed-cycles.js'));
        const auth = sources.findIndex((src) => src.includes('js/auth.js'));

        assert.ok(helper >= 0, `${path.basename(file)} charge auth.js sans managed-cycles.js`);
        assert.ok(helper < auth, `${path.basename(file)} : managed-cycles.js doit précéder auth.js`);
    });
}
