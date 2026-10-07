/**
 * Logique pure partagée par les onglets de l'écran Internat (lot G, tâche 5) — internat-helpers.js.
 * Testée sans navigateur : jauge d'occupation, libellés, corps de requête, validation de confort, traduction des codes d'erreur.
 * Le serveur reste le juge (FluentValidation, règles métier) : la validation client n'est qu'un confort.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const helpers = (api) => loadScripts(['internat-helpers.js'], api ? { preload: { api } } : {}).window.internatHelpers;

// ---------------------------------------------------------------- jauge d'occupation

test('occupancyPercent : bornes, arrondi et capacité nulle sans division par zéro', () => {
    const h = helpers();

    assert.equal(h.occupancyPercent(0, 10), 0);
    assert.equal(h.occupancyPercent(5, 10), 50);
    assert.equal(h.occupancyPercent(1, 3), 33);
    assert.equal(h.occupancyPercent(10, 10), 100);
    assert.equal(h.occupancyPercent(12, 10), 100, 'jamais plus de 100 %');
    assert.equal(h.occupancyPercent(3, 0), 0, 'capacité nulle : pas de division par zéro');
    assert.equal(h.occupancyPercent(undefined, 10), 0);
});

test('occupancyColor : vide / en remplissage / proche de la capacité (≥ 80 %) / complet', () => {
    const h = helpers();

    assert.equal(h.occupancyColor(0, 10), 'bg-slate-300');
    assert.equal(h.occupancyColor(0, 0), 'bg-slate-300');
    assert.equal(h.occupancyColor(7, 10), 'bg-emerald-500');
    assert.equal(h.occupancyColor(79, 100), 'bg-emerald-500', 'juste sous le seuil');
    assert.equal(h.occupancyColor(8, 10), 'bg-amber-500', 'exactement 80 %');
    assert.equal(h.occupancyColor(9, 10), 'bg-amber-500');
    assert.equal(h.occupancyColor(10, 10), 'bg-rose-500');
    assert.equal(h.occupancyColor(11, 10), 'bg-rose-500');
});

// ---------------------------------------------------------------- libellés

test('le genre « Mixte » n\'est jamais proposé à la saisie (réservé à la reprise de données, Q2)', () => {
    const h = helpers();

    assert.deepEqual(plain(h.GENDER_CHOICES.map((g) => g.value)), ['Garcons', 'Filles']);
    assert.equal(h.genderLabel('Garcons'), 'Garçons');
    assert.equal(h.genderLabel('Filles'), 'Filles');
    assert.match(h.genderLabel('Mixte'), /Mixte/, 'un pavillon repris reste affichable pour que le Directeur le qualifie');
    assert.equal(h.genderLabel(undefined), '');
});

test('bedStatusLabel : libellés français des trois états', () => {
    const h = helpers();

    assert.equal(h.bedStatusLabel('Available'), 'Disponible');
    assert.equal(h.bedStatusLabel('Occupied'), 'Occupé');
    assert.equal(h.bedStatusLabel('Maintenance'), 'En maintenance');
    assert.equal(h.bedStatusLabel('Inconnu'), 'Inconnu');
});

// ---------------------------------------------------------------- corps de requête

test('buildDormitoryBody : nettoie les champs, remplace les vides par null, ne contient jamais de genre Mixte', () => {
    const h = helpers();

    assert.deepEqual(
        plain(h.buildDormitoryBody({ name: '  Pavillon Oustaz Ahmad ', gender: 'Garcons', supervisorName: ' M. Ndiaye ', supervisorPhone: '', notes: '   ' })),
        { name: 'Pavillon Oustaz Ahmad', gender: 'Garcons', supervisorName: 'M. Ndiaye', supervisorPhone: null, supervisorUserId: null, notes: null });
});

test('buildDormitoryBody : la modification porte le rowVersion, la création non', () => {
    const h = helpers();
    const form = { name: 'P', gender: 'Filles' };

    assert.equal('rowVersion' in h.buildDormitoryBody(form), false);
    assert.equal(h.buildDormitoryBody(form, 42).rowVersion, 42);
    assert.equal(h.buildDormitoryBody(form, 0).rowVersion, 0, 'un jeton à 0 est un jeton (xmin) valide');
});

test('buildDormitoryBody : conserve le compte surveillant lié déjà enregistré', () => {
    const h = helpers();

    assert.equal(h.buildDormitoryBody({ name: 'P', gender: 'Filles', supervisorUserId: 'u-1' }, 3).supervisorUserId, 'u-1');
});

test('buildRoomBody : entier de lits, nom nettoyé', () => {
    const h = helpers();

    assert.deepEqual(plain(h.buildRoomBody('d-1', ' Chambre 101 ', '4')), { dormitoryId: 'd-1', name: 'Chambre 101', bedCount: 4 });
});

// ---------------------------------------------------------------- validation de confort

test('validateDormitory : mêmes bornes que le serveur (nom ≤ 100, surveillant ≤ 150, téléphone ≤ 30, notes ≤ 1000)', () => {
    const h = helpers();
    const ok = { name: 'Pavillon A', gender: 'Garcons' };

    assert.deepEqual(plain(h.validateDormitory(ok)), {});
    assert.ok(h.validateDormitory({ ...ok, name: '   ' }).name, 'nom obligatoire');
    assert.ok(h.validateDormitory({ ...ok, name: 'x'.repeat(101) }).name);
    assert.deepEqual(plain(h.validateDormitory({ ...ok, name: 'x'.repeat(100) })), {});
    assert.ok(h.validateDormitory({ ...ok, gender: 'Mixte' }).gender, 'Mixte refusé à la saisie');
    assert.ok(h.validateDormitory({ ...ok, gender: '' }).gender);
    assert.ok(h.validateDormitory({ ...ok, supervisorName: 'x'.repeat(151) }).supervisorName);
    assert.ok(h.validateDormitory({ ...ok, supervisorPhone: '7'.repeat(31) }).supervisorPhone);
    assert.ok(h.validateDormitory({ ...ok, notes: 'x'.repeat(1001) }).notes);
});

test('validateRoom : nom obligatoire, 1 à 40 lits entiers', () => {
    const h = helpers();

    assert.deepEqual(plain(h.validateRoom({ name: 'Chambre 1', bedCount: 4 })), {});
    assert.ok(h.validateRoom({ name: '', bedCount: 4 }).name);
    assert.ok(h.validateRoom({ name: 'C', bedCount: 0 }).bedCount);
    assert.ok(h.validateRoom({ name: 'C', bedCount: 41 }).bedCount);
    assert.deepEqual(plain(h.validateRoom({ name: 'C', bedCount: 40 })), {});
    assert.ok(h.validateRoom({ name: 'C', bedCount: 2.5 }).bedCount, 'pas de lit fractionnaire');
    assert.ok(h.validateRoom({ name: 'C', bedCount: 'abc' }).bedCount);
});

// ---------------------------------------------------------------- traduction des codes d'erreur

const CODES = [
    ['RESOURCE_IN_USE', /encore/i],
    ['ACTIVE_ENTITY_CONFLICT', /déjà/i],
    ['ARCHIVED_ENTITY_EXISTS', /restaur/i],
    ['PARENT_ENTITY_ARCHIVED', /parent|d'abord/i],
    ['CONCURRENCY_CONFLICT', /modifié/i],
    ['BED_UNAVAILABLE', /lit/i],
    ['MODULE_DISABLED', /module/i]
];

for (const [code, pattern] of CODES) {
    test(`errorMessage : le code ${code} est traduit en français actionnable`, () => {
        const h = helpers({ toMessage: () => 'générique' });

        const message = h.errorMessage({ code, status: 409, message: 'technical english' }, 'repli');

        assert.match(message, pattern);
        assert.doesNotMatch(message, /technical english/);
    });
}

test('errorMessage : un code inconnu retombe sur le message de l\'API, puis sur le repli', () => {
    const h = helpers({ toMessage: (err, fallback) => (err && err.message) || fallback });

    assert.equal(h.errorMessage({ code: 'AUTRE', message: 'Message serveur' }, 'repli'), 'Message serveur');
    assert.equal(h.errorMessage({ code: 'AUTRE' }, 'repli'), 'repli');
    assert.equal(h.errorMessage(null, 'repli'), 'repli');
});

test('errorMessage : une erreur déjà affichée par la modale « Accès refusé » ne double pas le message', () => {
    const h = helpers({ toMessage: () => '' });

    assert.equal(h.errorMessage({ status: 403, handledGlobally: true }, 'repli'), '');
});

test('errorMessage : fonctionne même si window.api n\'est pas chargé', () => {
    const h = helpers();

    assert.equal(h.errorMessage({ code: 'RESOURCE_IN_USE' }, 'repli').length > 0, true);
    assert.equal(h.errorMessage({ code: 'INCONNU', message: 'M' }, 'repli'), 'M');
});

// ---------------------------------------------------------------- onglets de l'écran

test("tabFromHash : l'onglet ouvert suit le fragment d'URL, avec repli sur Occupation", () => {
    const h = helpers();

    assert.equal(h.tabFromHash('#pavillons'), 'pavillons');
    assert.equal(h.tabFromHash('#occupation'), 'occupation');
    assert.equal(h.tabFromHash('#pensionnaires'), 'pensionnaires');
    assert.equal(h.tabFromHash('#sorties'), 'sorties');
    assert.equal(h.tabFromHash('#PAVILLONS'), 'pavillons', 'insensible à la casse');
    assert.equal(h.tabFromHash(''), 'occupation');
    assert.equal(h.tabFromHash(undefined), 'occupation');
    assert.equal(h.tabFromHash('#inconnu'), 'occupation', "un fragment inconnu ne casse pas l'écran");
    assert.equal(h.tabFromHash('#pavillons?x=1'), 'occupation');
});

test('TABS : liste ordonnée des onglets disponibles', () => {
    const h = helpers();

    assert.deepEqual(plain(h.TABS.map((t) => t.key)), ['occupation', 'pavillons', 'pensionnaires', 'sorties']);
    assert.ok(h.TABS.every((t) => t.label));
});
