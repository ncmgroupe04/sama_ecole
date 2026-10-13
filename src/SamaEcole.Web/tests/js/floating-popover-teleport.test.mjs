/**
 * Garde-fou : les menus flottants (<select-field>, <date-field>) doivent être téléportés sous <body>.
 *
 * Régression d'origine : le panneau de <modal-shell> porte `backdrop-blur-xl` (verre dépoli) et
 * `overflow-hidden`. Un `backdrop-filter` fait de son porteur le repère des descendants
 * `position: fixed` : le menu « Niveau » du formulaire de classe, calculé en coordonnées viewport par
 * window.computeFloatingPosition, était décalé de la distance écran→fenêtre (énorme sur grand écran)
 * puis rogné par le panneau — la liste devenait invisible. Seul x-teleport="body" l'en protège pour de
 * bon, quel que soit l'effet de style qu'on ajoutera plus tard à un ancêtre.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const TAG_HELPERS = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'TagHelpers');

for (const file of ['SelectFieldTagHelper.cs', 'DateFieldTagHelper.cs']) {
    test(`${file} : le menu flottant est téléporté sous <body>, au-dessus de la modale`, () => {
        const source = readFileSync(path.join(TAG_HELPERS, file), 'utf8');
        const popover = source.match(/<template x-teleport="body">\s*<div x-show="open"[^>]*?class="([^"]*)"/s);
        assert.ok(popover, `${file} : le <div x-show="open"> doit être enfant direct de <template x-teleport="body">`);
        const classes = popover[1].split(/\s+/);
        assert.ok(classes.includes('fixed'), `${file} : le menu doit rester position: fixed`);
        const z = classes.find((c) => /^z-\[(\d+)\]$/.test(c));
        assert.ok(z, `${file} : z-index explicite requis (le menu quitte le contexte d'empilement de la modale)`);
        const zValue = Number(z.match(/\d+/)[0]);
        assert.ok(zValue > 50 && zValue < 60, `${file} : ${z} doit dépasser la modale (z-50) et rester sous les toasts (z-[60])`);
    });
}
