/**
 * Garde-fou : le bouton « Démarrage » (assistant de premier paramétrage) doit rester monté dans la barre
 * supérieure de _Layout.cshtml. Une refonte de la barre l'avait retiré sans bruit : le composant JS existait
 * toujours, mais plus rien ne l'instanciait — le bloc « Niveau de configuration » de Paramètres restait sur
 * « Analyse… » et l'avertissement du mode test (qui attend `setup-progress`) ne s'ouvrait plus.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const layout = readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'Views', 'Shared', '_Layout.cshtml'), 'utf8');

test('la barre supérieure instancie setupAssistant() avec son bouton « Démarrage » et « Revérifier »', () => {
    assert.match(layout, /x-data="setupAssistant\(\)"/);
    assert.match(layout, /<span class="hidden sm:block">Démarrage<\/span>/);
    assert.match(layout, /Revérifier/);
});

test('une seule instance de setupAssistant() est montée', () => {
    assert.equal((layout.match(/x-data="setupAssistant\(\)"/g) || []).length, 1);
});
