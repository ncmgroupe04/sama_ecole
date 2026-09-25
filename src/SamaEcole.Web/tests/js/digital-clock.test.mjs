/**
 * Pastille date/heure de la barre supérieure (`digitalClock`, wwwroot/js/ui-components.js) :
 * le mois est abrégé quand il est long (« sept. »), pour resserrer le pill.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function clockAt(iso) {
    const RealDate = Date;
    class FrozenDate extends RealDate {
        constructor(...args) { args.length ? super(...args) : super(iso); }
    }
    const ctx = loadScripts(['ui-components.js']);
    ctx.sandbox.Date = FrozenDate;
    ctx.sandbox.setInterval = () => 0; // pas de minuterie réelle : elle garderait node en vie
    const clock = ctx.component('digitalClock');
    clock.updateClock();
    return clock.currentTime;
}

test('un mois long est abrégé', () => {
    assert.equal(clockAt('2026-09-25T01:44:00'), 'Vendredi 25 sept. 2026 • 01:44');
});

test('un mois court reste entier', () => {
    assert.equal(clockAt('2026-06-05T14:03:00'), 'Vendredi 5 juin 2026 • 14:03');
});
