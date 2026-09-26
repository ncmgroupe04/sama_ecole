/**
 * Grille tarifaire de la vitrine (#tarifs) — comportement d'onglets et de filtre côté navigateur.
 *
 *   1. « Établissements publics » est l'onglet ouvert par défaut ; le privé se choisit au clic.
 *   2. Le filtre de cycles (monocycle / bicycle / complexe) ne montre qu'un groupe ; « tous » les montre.
 *   3. Changer d'onglet remet le filtre sur « tous » : on n'arrive jamais sur un onglet vide.
 *
 * Le contenu (montants, paliers) est rendu côté serveur par MarketingCatalog.Pricing : ce composant ne fait
 * qu'alterner et filtrer du DOM existant.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

// marketing.js interroge le DOM au chargement (apparition au défilement) : une page sans élément `[data-reveal]`.
const pricing = () => loadScripts(['marketing.js'], {
    document: { readyState: 'complete', querySelectorAll: () => [] }
}).component('marketingPricing');

test('le composant s\'ouvre sur les établissements publics, tous cycles confondus', () => {
    const view = pricing();

    assert.equal(view.isAudience('public'), true);
    assert.equal(view.isAudience('prive'), false);
    assert.equal(view.isGroup('tous'), true);
});

test('choisir le privé bascule l\'onglet', () => {
    const view = pricing();

    view.selectAudience('prive');

    assert.equal(view.isAudience('prive'), true);
    assert.equal(view.isAudience('public'), false);
});

test('le filtre de cycles n\'affiche que le groupe choisi', () => {
    const view = pricing();
    view.selectAudience('prive');

    view.selectGroup('bicycle');

    assert.equal(view.shows('bicycle'), true);
    assert.equal(view.shows('monocycle'), false);
    assert.equal(view.shows('complexe'), false);
});

test('« tous les cycles » affiche chaque groupe', () => {
    const view = pricing();
    view.selectAudience('prive');
    view.selectGroup('complexe');

    view.selectGroup('tous');

    ['monocycle', 'bicycle', 'complexe'].forEach((key) => assert.equal(view.shows(key), true, key));
});

test('changer d\'onglet remet le filtre sur « tous »', () => {
    const view = pricing();
    view.selectAudience('prive');
    view.selectGroup('bicycle');

    view.selectAudience('public');

    assert.equal(view.isGroup('tous'), true);
    assert.equal(view.shows('monocycle'), true);
});
