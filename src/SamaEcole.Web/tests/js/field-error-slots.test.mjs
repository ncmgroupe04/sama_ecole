/**
 * Emplacements d'erreur par champ des formulaires qui passent par `toFieldErrors`.
 *
 * Un 422 porte une clé par champ refusé (`GuardianPhone`…). Sans `<p x-text="xxxErrors.<clé>">` sous le champ, l'erreur est
 * reçue puis jetée et le bouton « Enregistrer » semble sans effet (cause du signalement de l'inscription d'un élève). Le
 * filet central d'api.js (surfaceUnmappedErrors) rattrape ce cas dans le bandeau ; ces emplacements restent la bonne
 * réponse : le message apparaît SOUS le champ fautif. Texte libre validé par le serveur (longueur, anti-HTML, > 0).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const view = (path) => readFile(new URL(`../../Views/${path}`, import.meta.url), 'utf8');

const SLOTS = [
    ['Inventory/Index.cshtml', ['createItemErrors.locationlabel', 'createItemErrors.notes', 'editItemErrors.locationlabel', 'editItemErrors.notes']],
    ['Subjects/Index.cshtml', ['editErrors.column1header', 'editErrors.column2header']],
    ['Exams/Index.cshtml', ['editDossierErrors.civilstatusnotes']],
    ['SuperAdmin/Pricing.cshtml', ['createErrors.maxuses']]
];

for (const [path, expressions] of SLOTS) {
    for (const expression of expressions) {
        test(`${path} : l'erreur « ${expression} » s'affiche sous son champ`, async () => {
            const html = await view(path);
            assert.ok(html.includes(`x-text="${expression}"`), `aucun x-text="${expression}" dans ${path}`);
        });
    }
}
