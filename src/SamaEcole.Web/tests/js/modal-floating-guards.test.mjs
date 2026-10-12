/**
 * Garde-fous des FENÊTRES (<modal-shell>) et de ce qui s'y ouvre : aucune liste ne doit déborder, être rognée ou
 * disparaître derrière la fenêtre, et Échap ferme d'abord la liste, jamais toute la fenêtre d'un coup.
 *
 * Contexte : le panneau de <modal-shell> porte `backdrop-blur-xl` + `overflow-hidden`. Tout descendant en
 * `position: absolute` ou `fixed` y est rogné ou décalé. Les menus de <select-field> et <date-field> sont téléportés sous
 * <body> (voir floating-popover-teleport.test.mjs) ; les listes d'autocomplétion, elles, doivent rester DANS LE FLUX
 * (elles poussent le contenu qui les suit au lieu de le recouvrir) — jamais `absolute`.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

function walk(dir, ext) {
    return readdirSync(dir).flatMap((name) => {
        const full = path.join(dir, name);
        if (statSync(full).isDirectory()) return name === 'node_modules' || name === 'bin' || name === 'obj' ? [] : walk(full, ext);
        return full.endsWith(ext) ? [full] : [];
    });
}

test('Échap ferme la liste ou le calendrier ouvert AVANT la fenêtre qui les contient', () => {
    for (const file of ['SelectFieldTagHelper.cs', 'DateFieldTagHelper.cs']) {
        const source = readFileSync(path.join(ROOT, 'TagHelpers', file), 'utf8');
        // Phase de CAPTURE sur window : elle passe avant l'écouteur de <modal-shell> (phase de remontée), et stopPropagation
        // l'empêche de fermer la fenêtre quand une liste est ouverte.
        assert.match(
            source,
            /x-on:keydown\.escape\.window\.capture="open && \(\$event\.stopPropagation\(\), close\(\)\)"/,
            `${file} : Échap doit fermer le menu ouvert et s'arrêter là (capture + stopPropagation), sans fermer la fenêtre`
        );
    }
});

/** Une liste « déroulante » posée en absolu : ombre/anneau + z-index + hauteur bornée ou défilement. */
const ABSOLUTE_DROPDOWN = /class="([^"]*\babsolute\b[^"]*)"/g;
function looksLikeDropdown(classes) {
    const c = classes.split(/\s+/);
    const hasZ = c.some((x) => /^z-(\d+|\[\d+\])$/.test(x));
    const hasShadow = c.some((x) => /^shadow(-[a-z]+)?$/.test(x) || x === 'ring-1');
    const scrolls = c.some((x) => x.startsWith('max-h-') || x.startsWith('overflow-'));
    return hasZ && hasShadow && scrolls;
}

test('aucune liste déroulante en position absolue dans une fenêtre : elle serait rognée par le panneau', () => {
    const offenders = [];
    for (const file of walk(path.join(ROOT, 'Views'), '.cshtml')) {
        const source = readFileSync(file, 'utf8');
        for (const block of source.matchAll(/<modal-shell\b[\s\S]*?<\/modal-shell>/g)) {
            for (const m of block[0].matchAll(ABSOLUTE_DROPDOWN)) {
                if (looksLikeDropdown(m[1])) offenders.push(`${path.relative(ROOT, file)} : class="${m[1].slice(0, 90)}…"`);
            }
        }
    }
    assert.deepEqual(
        offenders,
        [],
        'Liste en position absolue DANS un <modal-shell> : le panneau (overflow + verre dépoli) la rogne ou la décale. Retirer ' +
        '`absolute z-*` pour la laisser dans le flux (elle poussera le contenu suivant), ou utiliser <select-field>.\n  ' + offenders.join('\n  ')
    );
});

test('le garde-fou reconnaît bien une liste absolue (il ne rassure pas à tort)', () => {
    assert.equal(looksLikeDropdown('absolute z-10 w-full bg-white border mt-1 shadow-lg max-h-48 overflow-auto'), true);
    assert.equal(looksLikeDropdown('absolute z-20 mt-1 w-full max-h-56 overflow-y-auto rounded-lg bg-white shadow-lg ring-1'), true);
    assert.equal(looksLikeDropdown('w-full bg-white border mt-1 shadow-sm max-h-48 overflow-auto'), false, 'dans le flux : correct');
    assert.equal(looksLikeDropdown('absolute right-3 top-3 z-10 flex h-8 w-8'), false, 'un bouton fermer n\'est pas une liste');
});

/**
 * Toutes les fenêtres partagent la MÊME apparence (verre dépoli) parce qu'elles passent par <modal-shell>. Une vue qui
 * recompose sa propre fenêtre (`fixed inset-0` + panneau) en réintroduit une différente. Exceptions documentées :
 *  - Halqa.cshtml : tiroir de saisie des quarts, déjà en verre dépoli, conçu pour la tablette de l'Oustaz ;
 *  - AnnuaireB2C.cshtml : fiche publique, qui reprend la même surface mais ne charge pas ui-components.js.
 */
const OWN_DIALOG_ALLOWED = new Set(['Internat/Halqa.cshtml', 'Home/AnnuaireB2C.cshtml']);

test('aucune vue ne recompose sa propre fenêtre : toutes passent par <modal-shell>', () => {
    const offenders = [];
    for (const file of walk(path.join(ROOT, 'Views'), '.cshtml')) {
        const rel = path.relative(path.join(ROOT, 'Views'), file).split(path.sep).join('/');
        if (OWN_DIALOG_ALLOWED.has(rel)) continue;
        const source = readFileSync(file, 'utf8');
        // Un fond plein écran de fenêtre : `fixed inset-0` à z-40 / z-50 / z-[60] (les voiles de menu latéral sont à z-20).
        for (const m of source.matchAll(/class="[^"]*\bfixed inset-0 z-(?:40|50|\[60\])\b[^"]*"/g)) {
            offenders.push(`${rel} : ${m[0].slice(0, 80)}…`);
        }
    }
    assert.deepEqual(
        offenders,
        [],
        'Fenêtre artisanale (`fixed inset-0`) : utiliser <modal-shell> (theme="dark" dans la console SuperAdmin) pour garder ' +
        'la même apparence et les mêmes comportements (Échap, fond, défilement figé).\n  ' + offenders.join('\n  ')
    );
});

test('les fenêtres de la console SuperAdmin utilisent la variante sombre du composant commun', () => {
    for (const rel of ['SuperAdmin/Schools.cshtml', 'SuperAdmin/Pricing.cshtml', 'SuperAdmin/Billing.cshtml', 'Admin/RegistrationRequests.cshtml',
        'Shared/_ChangePasswordModalDark.cshtml', 'Shared/_ChangeEmailModalDark.cshtml']) {
        const source = readFileSync(path.join(ROOT, 'Views', rel), 'utf8');
        const shells = [...source.matchAll(/<modal-shell\s+open=[^>]*>/g)].map((m) => m[0]);
        assert.ok(shells.length > 0, `${rel} : au moins une <modal-shell>`);
        for (const tag of shells) assert.match(tag, /theme="dark"/, `${rel} : ${tag.slice(0, 70)} doit porter theme="dark" (interface sombre)`);
    }
});
