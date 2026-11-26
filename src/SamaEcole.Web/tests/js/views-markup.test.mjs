/**
 * Garde-fou de balisage des vues Razor : une balise dont le `>` d'ouverture manque
 * (`x-text="…"</div>`) est lue par le navigateur comme un attribut — l'élément n'est jamais refermé et
 * toute la suite de la page s'imbrique dedans (fiche enseignant déformée, champs superposés). Aucun
 * compilateur ne le signale : ce test le fait.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const viewsDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'Views');

function* cshtmlFiles(dir) {
    for (const name of readdirSync(dir)) {
        const p = join(dir, name);
        if (statSync(p).isDirectory()) yield* cshtmlFiles(p);
        else if (name.endsWith('.cshtml')) yield p;
    }
}

test('no Razor view has an attribute value directly followed by a closing tag', () => {
    const malformed = /[A-Za-z:@.\-]+="[^"<>]*"<\/[a-zA-Z]+>/;
    const offenders = [];
    for (const file of cshtmlFiles(viewsDir)) {
        readFileSync(file, 'utf8').split(/\r?\n/).forEach((line, i) => {
            if (malformed.test(line)) offenders.push(`${file.slice(viewsDir.length + 1)}:${i + 1}`);
        });
    }
    assert.deepEqual(offenders, [], `balise sans « > » avant la fermeture : ${offenders.join(', ')}`);
});
