/**
 * Suppression guidée (wwwroot/js/guided-delete.js) — composant PARTAGÉ par les écrans qui suppriment un élément.
 *
 * Règles verrouillées ici : la phase d'affichage suit l'analyse serveur (blocage financier sans option de rupture,
 * liens à rompre, confirmation simple) ; la rupture exige la case de confirmation ; la rupture et la suppression sont
 * DEUX appels distincts, dans cet ordre ; un lien apparu entre-temps ramène à l'analyse à jour avec le message serveur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const TARGET = { id: 's-1', name: 'Awa Fall', rowVersion: 42 };

const IMPACT_LINKS = {
    canDelete: false, financialBlock: false,
    message: 'Cet élève est actuellement rattaché à 1 inscription. Vous devez rompre ces liens avant de pouvoir effectuer la suppression.',
    links: [{ kind: 'Enrollment', label: '1 inscription', count: 1, breakable: true, breakAction: "Annuler l'inscription et l'archiver" }]
};
const IMPACT_FINANCIAL = {
    canDelete: false, financialBlock: true,
    message: 'Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.', links: []
};
const IMPACT_FREE = { canDelete: true, financialBlock: false, message: null, links: [] };

function mixin({ impacts, calls = [], failDelete } = {}) {
    const queue = [...impacts];
    const api = {
        get: async (url) => { calls.push(['GET', url]); return queue.length > 1 ? queue.shift() : queue[0]; },
        post: async (url, body) => { calls.push(['POST', url, body]); return {}; },
        delete: async (url) => {
            calls.push(['DELETE', url]);
            if (failDelete) throw failDelete;
            return {};
        }
    };
    const ctx = loadScripts(['guided-delete.js'], { preload: { api } });
    const state = ctx.window.guidedDelete.mixin({ resource: 'students' });
    state.afterGuidedDelete = async () => { state.refreshed = true; };
    return { state, calls };
}

test('la phase suit l\'analyse : blocage financier, liens à rompre, confirmation simple', async () => {
    for (const [impact, phase] of [[IMPACT_FINANCIAL, 'financial'], [IMPACT_LINKS, 'links'], [IMPACT_FREE, 'ready']]) {
        const { state, calls } = mixin({ impacts: [impact] });
        await state.openGuidedDelete(TARGET);
        assert.equal(state.guidedDelete.phase, phase);
        assert.deepEqual(plain(calls[0]), ['GET', '/students/s-1/deletion-impact']);
    }
});

test('les liens sont rompus puis la fiche supprimée : deux appels distincts, dans cet ordre, après confirmation', async () => {
    const { state, calls } = mixin({ impacts: [IMPACT_LINKS] });
    await state.openGuidedDelete(TARGET);
    state.guidedDelete.confirmed = true;

    await state.confirmGuidedDelete();
    await flush();

    assert.deepEqual(plain(calls.slice(1)), [
        ['POST', '/students/s-1/unlink-dependencies', {}],
        ['DELETE', '/students/s-1?rowVersion=42']
    ]);
    assert.equal(state.guidedDelete, null, 'la fenêtre se ferme');
    assert.equal(state.refreshed, true, 'l\'écran hôte relit sa liste');
});

test('sans la case de confirmation cochée, rien n\'est rompu ni supprimé', async () => {
    const { state, calls } = mixin({ impacts: [IMPACT_LINKS] });
    await state.openGuidedDelete(TARGET);

    await state.confirmGuidedDelete();

    assert.equal(calls.length, 1, 'seule l\'analyse a été appelée');
    assert.match(state.guidedDelete.error, /Cochez la case/);
});

test('sans aucun lien, la suppression ne passe pas par la rupture', async () => {
    const { state, calls } = mixin({ impacts: [IMPACT_FREE] });
    await state.openGuidedDelete(TARGET);

    await state.confirmGuidedDelete();
    await flush();

    assert.deepEqual(plain(calls.slice(1)), [['DELETE', '/students/s-1?rowVersion=42']]);
});

test('en blocage financier, aucune suppression ni rupture n\'est possible depuis la fenêtre', async () => {
    const { state, calls } = mixin({ impacts: [IMPACT_FINANCIAL] });
    await state.openGuidedDelete(TARGET);

    await state.confirmGuidedDelete();
    await flush();

    // La fenêtre ne propose pas le bouton, et même appelée la méthode n'envoie rien : seule l'analyse a été faite.
    assert.equal(calls.length, 1);
    assert.equal(state.guidedDelete.phase, 'financial');
});

test('un lien apparu entre-temps ramène à l\'analyse à jour, avec le message du serveur', async () => {
    const failure = Object.assign(new Error('Cet élève est actuellement rattaché à 1 inscription et 2 retards.'), { code: 'DEPENDENCIES_EXIST' });
    const { state, calls } = mixin({ impacts: [IMPACT_FREE, IMPACT_LINKS], failDelete: failure });
    await state.openGuidedDelete(TARGET);
    assert.equal(state.guidedDelete.phase, 'ready');

    await state.confirmGuidedDelete();
    await flush();

    assert.equal(state.guidedDelete.phase, 'links', 'l\'analyse a été relue');
    assert.equal(state.guidedDelete.confirmed, false, 'la confirmation est à refaire');
    assert.match(state.guidedDelete.error, /1 inscription et 2 retards/);
    assert.equal(calls.filter(([method]) => method === 'GET').length, 2);
});

test('un lien non rompable (matériel prêté) masque le bouton de rupture', async () => {
    const impact = {
        canDelete: false, financialBlock: false, message: 'Cet élève est actuellement rattaché à 1 matériel prêté non restitué.',
        links: [{ kind: 'UnreturnedLoan', label: '1 matériel prêté non restitué', count: 1, breakable: false, breakAction: '' }]
    };
    const { state } = mixin({ impacts: [impact] });
    await state.openGuidedDelete(TARGET);

    assert.equal(state.guidedDelete.phase, 'links');
    assert.equal(state.guidedDeleteAllBreakable(), false);
});
