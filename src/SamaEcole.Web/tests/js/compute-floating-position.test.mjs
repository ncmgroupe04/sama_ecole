/**
 * Positionnement des popovers flottants (`computeFloatingPosition` / `floatingStyleFrom`,
 * wwwroot/js/ui-components.js). Ce calcul sert TOUS les <select-field> et <date-field> du produit :
 * un menu de genre, de classe, de mois, un calendrier de date de naissance… Les cas couverts ici
 * sont ceux qui, avant correctif, faisaient « chevaucher » le menu avec son champ ou le
 * détachaient loin au-dessus quand le champ approchait du bas de la fenêtre.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

const VIEWPORT_H = 800;
const VIEWPORT_W = 1200;

function ctx() {
    return loadScripts(['ui-components.js'], {
        preload: { innerHeight: VIEWPORT_H, innerWidth: VIEWPORT_W }
    }).window;
}

/** Faux déclencheur : seul getBoundingClientRect() est lu par le calcul. */
function anchor({ top, height = 44, left = 100, width = 260 }) {
    return {
        getBoundingClientRect: () => ({
            top, left, width, height, bottom: top + height, right: left + width
        })
    };
}

test('place assez basse : le menu s\'ouvre SOUS le champ, avec un décalage', () => {
    const w = ctx();
    const pos = w.computeFloatingPosition(anchor({ top: 120 }), 300);
    assert.equal(pos.openUp, false);
    assert.equal(pos.bottom, null);
    // 6 px sous le bord bas du champ (120 + 44) — jamais collé, jamais chevauchant.
    assert.equal(pos.top, 170);
});

test('champ vers le bas de la fenêtre, place au-dessus : bascule vers le HAUT, ancré par son bord bas', () => {
    const w = ctx();
    // Champ à 40 px du bas de la fenêtre : rien dessous, tout dessus.
    const a = anchor({ top: VIEWPORT_H - 40 - 44 }); // top = 716, bottom = 760
    const pos = w.computeFloatingPosition(a, 300);
    assert.equal(pos.openUp, true);
    assert.equal(pos.top, null);
    // Bord bas du menu = 6 px au-dessus du haut du champ (716) => bottom CSS = 800 - 716 + 6.
    assert.equal(pos.bottom, VIEWPORT_H - 716 + 6);
});

test('ouverture vers le haut : la hauteur max ne fait jamais déborder le menu au-dessus du viewport', () => {
    // Fenêtre courte (400) : peu de place dessous, un peu plus dessus => bascule vers le haut, et
    // maxHeight doit rester borné par la place réelle au-dessus (top - 6 - 8).
    const w = loadScripts(['ui-components.js'], { preload: { innerHeight: 400, innerWidth: VIEWPORT_W } }).window;
    const a = anchor({ top: 250 }); // bottom = 294 ; spaceBelow ~ 92 ; spaceAbove ~ 236
    const pos = w.computeFloatingPosition(a, 300);
    assert.equal(pos.openUp, true);
    assert.ok(pos.maxHeight <= 250 - 6 - 8 + 0.001, `maxHeight=${pos.maxHeight} déborde au-dessus`);
});

test('la hauteur max ne dépasse jamais la place réelle du côté choisi', () => {
    const w = ctx();
    // ~120 px sous le champ : le menu descendant doit se clamper là, pas rester à 300.
    const a = anchor({ top: VIEWPORT_H - 120 - 44 });
    const pos = w.computeFloatingPosition(a, 300);
    if (!pos.openUp) {
        assert.ok(pos.maxHeight <= 120 + 0.001, `maxHeight=${pos.maxHeight}`);
    }
});

test('clamp horizontal : un champ près du bord droit ne pousse pas le menu hors écran', () => {
    const w = ctx();
    const a = anchor({ top: 100, left: VIEWPORT_W - 80, width: 260 });
    const pos = w.computeFloatingPosition(a, 300);
    assert.ok(pos.left + pos.width <= VIEWPORT_W - 8 + 0.001, `left=${pos.left} width=${pos.width}`);
    assert.ok(pos.left >= 8);
});

test('sans déclencheur : objet de repli sûr, ouverture vers le bas', () => {
    const w = ctx();
    const pos = w.computeFloatingPosition(null, 300);
    assert.equal(pos.openUp, false);
    assert.equal(typeof pos.top, 'number');
    assert.equal(pos.bottom, null);
});

test('floatingStyleFrom émet top: vers le bas et bottom: vers le haut', () => {
    const w = ctx();
    const down = w.floatingStyleFrom({ openUp: false, top: 170, bottom: null, left: 100, width: 260, maxHeight: 300 });
    assert.ok(down.startsWith('top:170px'), down);
    assert.ok(!down.includes('bottom:'), down);

    const up = w.floatingStyleFrom({ openUp: true, top: null, bottom: 90, left: 100, width: 260, maxHeight: 240 });
    assert.ok(up.startsWith('bottom:90px'), up);
    assert.ok(!up.includes('top:'), up);
    assert.ok(up.includes('max-height:240px'), up);
});
