/**
 * Tracé des courbes d'évolution (wwwroot/js/trend-chart.js) : géométrie SVG pure, sans navigateur. Ces tests prouvent que la
 * courbe part et finit où les données le disent, que l'axe vertical s'arrondit sans jamais écraser la courbe, qu'un point isolé
 * ou une série vide ne casse rien, et que les périodes (3, 6, 12 mois, tout) se calculent depuis « aujourd'hui ».
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

function chart() {
    const ctx = loadScripts(['trend-chart.js']);
    return ctx.window.trendChart;
}

const DAY = 24 * 3600 * 1000;
const T0 = Date.UTC(2026, 8, 1);

test('l\'axe vertical s\'arrondit à une graduation lisible, au moins 10 %', () => {
    const { niceMax } = chart();
    assert.equal(niceMax(0), 10);
    assert.equal(niceMax(3.2), 10);
    assert.equal(niceMax(10), 10);
    assert.equal(niceMax(10.1), 20);
    assert.equal(niceMax(23), 25);
    assert.equal(niceMax(26), 50);
    assert.equal(niceMax(51), 75);
    assert.equal(niceMax(80), 100);
    assert.equal(niceMax(100), 100);
});

test('la courbe commence sur le bord gauche et finit sur le bord droit de la zone de tracé', () => {
    const { build } = chart();
    const c = plain(build([{ t: T0, v: 5 }, { t: T0 + 10 * DAY, v: 20 }], { width: 400, height: 200, left: 40, right: 10, top: 10, bottom: 30 }));

    assert.equal(c.dots[0].cx, 40);
    assert.equal(c.dots[1].cx, 390);
    assert.ok(c.dots[1].cy < c.dots[0].cy, 'une valeur plus haute est plus haute à l\'écran (y plus petit)');
    assert.match(c.path, /^M40(\.\d+)?,/);
    assert.equal(c.plot.y1, 170);
});

test('le haut de l\'axe suit la valeur maximale arrondie, et la courbe ne dépasse jamais la zone', () => {
    const { build } = chart();
    const c = plain(build([{ t: T0, v: 3 }, { t: T0 + DAY, v: 23 }], { width: 400, height: 200, left: 40, right: 10, top: 10, bottom: 30 }));

    assert.equal(c.yMax, 25);
    assert.ok(c.dots.every((d) => d.cy >= c.plot.y0 && d.cy <= c.plot.y1));
    assert.deepEqual(c.yTicks.map((t) => t.value), [0, 12.5, 25]);
});

test('un axe vertical imposé est respecté (la courbe du bulletin reste comparable d\'un élève à l\'autre)', () => {
    const { build } = chart();
    const c = plain(build([{ t: T0, v: 3 }], { yMax: 100 }));

    assert.equal(c.yMax, 100);
});

test('un point isolé est centré et une série vide ne produit ni trait ni point', () => {
    const { build } = chart();
    const one = plain(build([{ t: T0, v: 7 }], { width: 400, height: 200, left: 40, right: 10, top: 10, bottom: 30 }));
    assert.equal(one.dots.length, 1);
    assert.equal(one.dots[0].cx, 215, 'au milieu de la zone');
    assert.equal(one.path, '');

    const none = plain(build([], {}));
    assert.equal(none.dots.length, 0);
    assert.equal(none.path, '');
    assert.equal(none.area, '');
});

test('l\'aire sous la courbe se referme sur l\'axe horizontal', () => {
    const { build } = chart();
    const c = plain(build([{ t: T0, v: 5 }, { t: T0 + DAY, v: 8 }], { width: 400, height: 200, left: 40, right: 10, top: 10, bottom: 30 }));

    assert.match(c.area, /L390(\.\d+)?,170 L40,170 Z$/);
});

test('la période de bornes imposées étire l\'axe horizontal au-delà des données', () => {
    const { build } = chart();
    const c = plain(build([{ t: T0 + 5 * DAY, v: 5 }, { t: T0 + 6 * DAY, v: 6 }],
        { width: 400, height: 200, left: 40, right: 10, top: 10, bottom: 30, fromMs: T0, toMs: T0 + 10 * DAY }));

    assert.equal(c.dots[0].cx, 215, 'la moitié de la période');
    assert.ok(c.dots[1].cx > c.dots[0].cx);
});

test('les graduations horizontales portent le libellé fourni par l\'appelant', () => {
    const { build } = chart();
    const c = plain(build([{ t: T0, v: 5 }, { t: T0 + 30 * DAY, v: 9 }], { formatX: (t) => 'J' + Math.round((t - T0) / DAY) }));

    assert.deepEqual(c.xTicks.map((t) => t.label), ['J0', 'J30']);
});

test('les périodes se calculent depuis aujourd\'hui : 3, 6, 12 mois, ou tout', () => {
    const { periodRange } = chart();
    const today = new Date(Date.UTC(2026, 9, 15));

    assert.deepEqual(plain(periodRange('3m', today)), { from: '2026-07-15', to: '2026-10-15' });
    assert.deepEqual(plain(periodRange('6m', today)), { from: '2026-04-15', to: '2026-10-15' });
    assert.deepEqual(plain(periodRange('12m', today)), { from: '2025-10-15', to: '2026-10-15' });
    assert.deepEqual(plain(periodRange('all', today)), { from: null, to: '2026-10-15' });
    assert.deepEqual(plain(periodRange('inconnue', today)), { from: '2026-04-15', to: '2026-10-15' }, 'repli sur 6 mois');
});

test('le 31 d\'un mois ne déborde pas sur le mois suivant quand on recule', () => {
    const { periodRange } = chart();

    assert.equal(periodRange('3m', new Date(Date.UTC(2026, 4, 31))).from, '2026-02-28');
});

test('le SVG produit contient la courbe, l\'aire, un cercle par point et les graduations', () => {
    const { build, svg } = chart();
    const c = build([{ t: T0, v: 5 }, { t: T0 + DAY, v: 8 }, { t: T0 + 2 * DAY, v: 9 }], { formatX: () => 'J' });
    const out = svg(c, { label: 'courbe test' });

    assert.match(out, /^<svg [^>]*viewBox="0 0 640 220"[^>]*role="img"[^>]*aria-label="courbe test"/);
    assert.equal((out.match(/<circle /g) || []).length, 3);
    assert.equal((out.match(/<path /g) || []).length, 2, 'aire + courbe');
    assert.match(out, />10%</);
    assert.match(out, />5%</);
    assert.match(out, />0%</);
    assert.ok(out.endsWith('</svg>'));
});

test('le SVG n\'inclut jamais de balise issue d\'un libellé : tout est échappé', () => {
    const { build, svg } = chart();
    const c = build([{ t: T0, v: 5 }, { t: T0 + DAY, v: 8 }], { formatX: () => '<script>alert(1)</script>' });
    const out = svg(c, { label: '"><img src=x onerror=alert(1)>' });

    assert.ok(!out.includes('<script>'));
    assert.ok(!out.includes('<img'));
    assert.match(out, /&lt;script&gt;/);
});

test('une série vide ne produit aucun SVG', () => {
    const { build, svg } = chart();

    assert.equal(svg(build([]), {}), '');
});
