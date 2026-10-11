/**
 * Sélecteur de thème (wwwroot/js/theme.js) : store Alpine « theme » — clair / sombre / système.
 * theme-boot.js (anti-flash) pose `.dark` avant le rendu ; ces tests prouvent que le store applique et mémorise
 * le choix, suit le thème du système SEULEMENT en mode « système », et retombe sur le clair par défaut.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

/** Charge theme.js avec un faux <html>, un faux matchMedia pilotable et, si besoin, un choix déjà mémorisé. */
function setup({ stored = null, systemDark = false } = {}) {
    const classes = new Set();
    const html = {
        classList: {
            toggle: (name, force) => (force ? classes.add(name) : classes.delete(name)),
            contains: (name) => classes.has(name)
        }
    };
    const listeners = [];
    const media = {
        matches: systemDark,
        addEventListener: (type, cb) => { if (type === 'change') listeners.push(cb); }
    };
    const env = loadScripts(['theme.js'], {
        document: { documentElement: html },
        preload: { matchMedia: () => media }
    });
    if (stored !== null) env.localStorage.setItem('sama-theme', stored);
    return {
        env, html, media,
        store: env.store('theme'),
        /** Simule un changement du thème du système. */
        setSystemDark(value) { media.matches = value; listeners.forEach((cb) => cb()); }
    };
}

test('par défaut : thème clair, rien n\'est posé sur <html>', () => {
    const { store, html } = setup();
    assert.equal(store.mode, 'light');
    assert.equal(html.classList.contains('dark'), false);
});

test('choisir « sombre » pose .dark, mémorise le choix et le restitue au rechargement', () => {
    const first = setup();
    first.store.set('dark');
    assert.equal(first.html.classList.contains('dark'), true);
    assert.equal(first.env.localStorage.getItem('sama-theme'), 'dark');

    const reload = setup({ stored: 'dark' });
    assert.equal(reload.store.mode, 'dark');
    assert.equal(reload.html.classList.contains('dark'), true);
});

test('revenir à « clair » retire .dark', () => {
    const { store, html } = setup({ stored: 'dark' });
    store.set('light');
    assert.equal(html.classList.contains('dark'), false);
    assert.equal(store.isDark, false);
});

test('« système » suit le thème du système en direct ; « clair » et « sombre » l\'ignorent', () => {
    const t = setup({ stored: 'system', systemDark: false });
    assert.equal(t.html.classList.contains('dark'), false);

    t.setSystemDark(true);
    assert.equal(t.html.classList.contains('dark'), true, 'le système passe en sombre → l\'app suit');
    t.setSystemDark(false);
    assert.equal(t.html.classList.contains('dark'), false);

    t.store.set('light');
    t.setSystemDark(true);
    assert.equal(t.html.classList.contains('dark'), false, 'choix « clair » : le système n\'a plus d\'effet');
});

test('une valeur mémorisée inconnue ou un mode invalide est ignoré', () => {
    const t = setup({ stored: 'violet' });
    assert.equal(t.store.mode, 'light');
    t.store.set('violet');
    assert.equal(t.store.mode, 'light', 'mode invalide refusé');
    assert.equal(t.env.localStorage.getItem('sama-theme'), 'violet', 'rien n\'a été réécrit');
});

test('un autre onglet qui change la préférence est suivi', () => {
    const t = setup();
    t.env.localStorage.setItem('sama-theme', 'dark');
    // L'événement `storage` réel porte la clé modifiée.
    t.env.window.dispatchEvent({ type: 'storage', key: 'sama-theme' });
    assert.equal(t.store.mode, 'dark');
    assert.equal(t.html.classList.contains('dark'), true);
});
