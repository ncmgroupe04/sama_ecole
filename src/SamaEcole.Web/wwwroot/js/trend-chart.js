/**
 * Courbes d'évolution — géométrie SVG pure, partagée par Ma Halqa (courbe d'un élève) et le Suivi coranique (progression moyenne
 * par mois). Aucune dépendance (pas de bibliothèque de graphiques à charger sur la tablette de l'Oustaz) ; le gabarit dessine le
 * <svg> avec ces coordonnées. Testée sans navigateur (tests/js/trend-chart.test.mjs).
 *
 * Vocabulaire : un POINT est { t, v } — `t` un instant en millisecondes, `v` une valeur en pourcentage du Coran.
 */
(function () {
    'use strict';

    /** Graduations « lisibles » de l'axe vertical, en %. Au moins 10 : une courbe à 2 % ne doit pas remplir tout le cadre. */
    const LADDER = [10, 20, 25, 50, 75, 100];

    /** Plus petite graduation qui contient `value` ; au-delà de 100, l'arrondi à la dizaine (cas des totaux en quarts). */
    function niceMax(value) {
        const v = Number.isFinite(value) ? value : 0;
        const step = LADDER.find((s) => s >= v);
        return step !== undefined ? step : Math.ceil(v / 10) * 10;
    }

    function round(n) {
        return Number(n.toFixed(1));
    }

    /**
     * Calcule le tracé.
     * @param {{t:number, v:number}[]} points  triés par `t`
     * @param {object} [options]
     * @param {number} [options.width=640] @param {number} [options.height=220]
     * @param {number} [options.left=44] @param {number} [options.right=16] @param {number} [options.top=12] @param {number} [options.bottom=28]
     * @param {number} [options.yMax]    axe vertical imposé (sinon arrondi à partir des données)
     * @param {number} [options.fromMs]  @param {number} [options.toMs]  bornes imposées de l'axe horizontal (sinon, celles des données)
     * @param {(t:number)=>string} [options.formatX]  libellé d'une graduation horizontale
     */
    function build(points, options) {
        const o = options || {};
        const width = o.width ?? 640;
        const height = o.height ?? 220;
        const plot = { x0: o.left ?? 44, y0: o.top ?? 12, x1: width - (o.right ?? 16), y1: height - (o.bottom ?? 28) };
        const list = points || [];
        const yMax = o.yMax ?? niceMax(list.reduce((max, p) => Math.max(max, p.v), 0));

        const fromMs = o.fromMs ?? (list.length ? list[0].t : 0);
        const toMs = o.toMs ?? (list.length ? list[list.length - 1].t : 0);
        const xOf = (t) => (toMs === fromMs ? (plot.x0 + plot.x1) / 2 : plot.x0 + ((t - fromMs) / (toMs - fromMs)) * (plot.x1 - plot.x0));
        const yOf = (v) => plot.y1 - (Math.min(Math.max(v, 0), yMax) / yMax) * (plot.y1 - plot.y0);

        const dots = list.map((p) => ({ cx: round(xOf(p.t)), cy: round(yOf(p.v)), t: p.t, v: p.v }));
        const line = dots.map((d, i) => (i === 0 ? 'M' : 'L') + d.cx + ',' + d.cy).join(' ');
        const hasLine = dots.length >= 2;
        const path = hasLine ? line : '';
        const area = hasLine
            ? line + ' L' + dots[dots.length - 1].cx + ',' + plot.y1 + ' L' + dots[0].cx + ',' + plot.y1 + ' Z'
            : '';

        const yTicks = [0, yMax / 2, yMax].map((value) => ({ value, y: round(yOf(value)) }));
        const format = o.formatX || ((t) => String(t));
        const xTicks = list.length === 0
            ? []
            : (fromMs === toMs ? [fromMs] : [fromMs, toMs]).map((t) => ({ x: round(xOf(t)), label: format(t) }));

        return { width, height, plot, yMax, path, area, dots, yTicks, xTicks };
    }

    function escapeHtml(text) {
        return String(text).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' }[c]));
    }

    /**
     * Rend le tracé en balise <svg> (chaîne), à injecter par `x-html` : une `<template x-for>` ne fonctionne pas dans un SVG (le
     * navigateur ne lui donne pas l'espace de noms SVG). Tout libellé est échappé — la chaîne ne contient jamais que des
     * nombres, des dates et le titre fourni. Chaîne vide quand il n'y a rien à tracer.
     * @param {object} chart   retour de build()
     * @param {object} [options] { label: titre accessible, stroke, fill, grid, text }
     */
    function svg(chart, options) {
        if (!chart.dots.length) return '';
        const o = options || {};
        const stroke = o.stroke || '#059669';
        const fill = o.fill || 'rgba(16,185,129,0.12)';
        const grid = o.grid || '#e2e8f0';
        const text = o.text || '#64748b';
        const { plot } = chart;

        const yLines = chart.yTicks.map((t) =>
            `<line x1="${plot.x0}" x2="${plot.x1}" y1="${t.y}" y2="${t.y}" stroke="${grid}" stroke-width="1"/>`
            + `<text x="${plot.x0 - 6}" y="${t.y + 4}" text-anchor="end" font-size="11" fill="${text}">${escapeHtml(t.value)}%</text>`).join('');
        const xLabels = chart.xTicks.map((t, i) =>
            `<text x="${t.x}" y="${chart.height - 8}" text-anchor="${chart.xTicks.length === 1 ? 'middle' : (i === 0 ? 'start' : 'end')}" font-size="11" fill="${text}">${escapeHtml(t.label)}</text>`).join('');
        const shapes = (chart.area ? `<path d="${chart.area}" fill="${fill}"/>` : '')
            + (chart.path ? `<path d="${chart.path}" fill="none" stroke="${stroke}" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round"/>` : '');
        const dots = chart.dots.map((d) => `<circle cx="${d.cx}" cy="${d.cy}" r="4" fill="${stroke}"/>`).join('');

        return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${chart.width} ${chart.height}" role="img" aria-label="${escapeHtml(o.label || '')}" class="h-auto w-full">`
            + yLines + shapes + dots + xLabels + '</svg>';
    }

    const MONTHS_BY_PERIOD = { '3m': 3, '6m': 6, '12m': 12, '24m': 24 };

    function isoDay(date) {
        return date.toISOString().slice(0, 10);
    }

    /**
     * Bornes (jours ISO, UTC) d'une période choisie par l'utilisateur, calculées depuis `today`. `all` : pas de borne basse (le
     * serveur part alors du début du journal). Une clé inconnue retombe sur six mois. Un 31 ne déborde jamais sur le mois suivant.
     */
    function periodRange(key, today) {
        const now = today || new Date();
        const to = isoDay(now);
        if (key === 'all') return { from: null, to };

        const months = MONTHS_BY_PERIOD[key] ?? 6;
        const year = now.getUTCFullYear();
        const monthIndex = now.getUTCMonth() - months;
        const target = new Date(Date.UTC(year, monthIndex, 1));
        const daysInTarget = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate();
        const day = Math.min(now.getUTCDate(), daysInTarget);
        return { from: isoDay(new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth(), day))), to };
    }

    window.trendChart = { niceMax, build, svg, periodRange };
})();
