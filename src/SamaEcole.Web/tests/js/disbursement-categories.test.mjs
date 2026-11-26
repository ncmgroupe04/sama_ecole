/**
 * Catégories de décaissement (formatters.js) — miroir de Domain.Enums.DisbursementCategory.
 * Le formulaire propose les dix catégories et les écrans d'affichage (historique, Trésorerie, tableau de
 * bord) lisent un libellé, jamais le nom brut de l'énumération.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { loadScripts } from './harness.mjs';

const ENUM_FILE = path.resolve(path.dirname(fileURLToPath(import.meta.url)),
    '..', '..', '..', 'SamaEcole.Domain', 'Enums', 'DisbursementCategory.cs');

test('le formulaire propose exactement les catégories de l\'énumération serveur', () => {
    const ctx = loadScripts(['formatters.js']);
    const source = readFileSync(ENUM_FILE, 'utf-8');
    const serverValues = [...source.matchAll(/^\s+([A-Z][A-Za-z]+),?\s*$/gm)].map((m) => m[1]);

    const clientValues = ctx.window.DISBURSEMENT_CATEGORIES.map((c) => c.value);
    assert.deepEqual([...clientValues].sort(), [...serverValues].sort());
});

test('un libellé lisible remplace le nom brut', () => {
    const ctx = loadScripts(['formatters.js']);
    assert.equal(ctx.window.disbursementCategoryLabel('ChargesFixes'), 'Charges Fixes');
    assert.equal(ctx.window.disbursementCategoryLabel('EauElectricite'), 'Eau et électricité');
});

test('une valeur inconnue est renvoyée telle quelle', () => {
    const ctx = loadScripts(['formatters.js']);
    assert.equal(ctx.window.disbursementCategoryLabel('Autre'), 'Autre');
});
