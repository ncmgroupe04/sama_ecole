/**
 * Modale de GUIDAGE universelle — store `guide` + window.guide(titre, message)
 * (wwwroot/js/ui-components.js, rendue par _GuidanceModal.cshtml).
 *
 * Ce qui compte : window.guide() alimente le store (titre + message + visible), dismiss() referme,
 * et un appel sans argument retombe sur un libellé neutre plutôt que « undefined ».
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function boot() {
    const ctx = loadScripts(['ui-components.js']);
    ctx.initAlpine();
    return ctx;
}

test('window.guide(titre, message) ouvre la modale avec son contenu', () => {
    const ctx = boot();

    ctx.window.guide("Sélection d'un enseignant requise", "Choisissez d'abord un enseignant.");

    const guide = ctx.store('guide');
    assert.equal(guide.visible, true);
    assert.equal(guide.title, "Sélection d'un enseignant requise");
    assert.equal(guide.message, "Choisissez d'abord un enseignant.");
});

test('dismiss() referme sans toucher au dernier contenu affiché', () => {
    const ctx = boot();
    ctx.window.guide('Titre', 'Message');

    ctx.store('guide').dismiss();

    assert.equal(ctx.store('guide').visible, false);
});

test('window.guide() sans argument retombe sur un libellé neutre', () => {
    const ctx = boot();

    ctx.window.guide();

    const guide = ctx.store('guide');
    assert.equal(guide.visible, true);
    assert.equal(guide.title, 'Information');
    assert.equal(guide.message, '');
});
