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

function mixin({ impacts, calls = [], failDelete, config = {}, resource = 'students' } = {}) {
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
    const state = ctx.window.guidedDelete.mixin({ resource, ...config });
    state.afterGuidedDelete = async (target) => { state.refreshed = true; state.deletedTarget = plain(target); };
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

test('le hook de fin reçoit la cible supprimée (le nom sert à annoncer la suppression)', async () => {
    const { state } = mixin({ impacts: [IMPACT_FREE] });
    await state.openGuidedDelete(TARGET);

    await state.confirmGuidedDelete();
    await flush();

    assert.deepEqual(state.deletedTarget, { id: 's-1', name: 'Awa Fall', rowVersion: 42, resource: 'students' });
});

test('la ressource pilote les routes : classes, matières, enseignants', async () => {
    for (const resource of ['classrooms', 'subjects', 'teachers']) {
        const { state, calls } = mixin({ impacts: [IMPACT_LINKS], resource });
        await state.openGuidedDelete({ id: 'x-1', name: 'Élément', rowVersion: 7 });
        state.guidedDelete.confirmed = true;

        await state.confirmGuidedDelete();
        await flush();

        assert.deepEqual(plain(calls.map(([method, url]) => [method, url])), [
            ['GET', `/${resource}/x-1/deletion-impact`],
            ['POST', `/${resource}/x-1/unlink-dependencies`],
            ['DELETE', `/${resource}/x-1?rowVersion=7`]
        ]);
    }
});

test("les textes d'aide ont une valeur par défaut et se personnalisent par écran", () => {
    const defaults = mixin({ impacts: [IMPACT_FREE] }).state.guidedDeleteTexts;
    assert.match(defaults.financialHint, /donnée financière/);
    assert.match(defaults.readyText, /supprimé/);

    const custom = mixin({ impacts: [IMPACT_FREE], config: { financialHint: 'Annulez les paiements.', readyText: 'Cette classe sera supprimée.' } }).state.guidedDeleteTexts;
    assert.equal(custom.financialHint, 'Annulez les paiements.');
    assert.equal(custom.readyText, 'Cette classe sera supprimée.');
});

test("une page peut supprimer plusieurs types d'éléments : la ressource se surcharge à l'ouverture", async () => {
    const { state, calls } = mixin({ impacts: [IMPACT_FREE], resource: 'buildings' });

    await state.openGuidedDelete({ id: 'r-1', name: 'Salle 1', rowVersion: 3, resource: 'rooms' });
    await state.confirmGuidedDelete();
    await flush();

    assert.deepEqual(plain(calls.map(([method, url]) => [method, url])), [
        ['GET', '/rooms/r-1/deletion-impact'],
        ['DELETE', '/rooms/r-1?rowVersion=3']
    ]);
    assert.equal(state.deletedTarget.resource, 'rooms');
});

test("une suppression sans jeton de version n'envoie aucun paramètre rowVersion", async () => {
    const { state, calls } = mixin({ impacts: [IMPACT_FREE], resource: 'finance/fee-categories' });

    await state.openGuidedDelete({ id: 'c-1', name: 'Scolarité' });
    await state.confirmGuidedDelete();
    await flush();

    assert.deepEqual(plain(calls[1]), ['DELETE', '/finance/fee-categories/c-1']);
});

test("les textes se surchargent à l'ouverture et reviennent aux valeurs de la page pour l'élément suivant", async () => {
    const { state } = mixin({ impacts: [IMPACT_FREE], config: { readyText: 'Texte de la page.' } });

    await state.openGuidedDelete({ ...TARGET, readyText: 'Texte de cet élément.' });
    assert.equal(state.guidedDeleteTexts.readyText, 'Texte de cet élément.');

    state.closeGuidedDelete();
    await state.openGuidedDelete(TARGET);
    assert.equal(state.guidedDeleteTexts.readyText, 'Texte de la page.');
});
