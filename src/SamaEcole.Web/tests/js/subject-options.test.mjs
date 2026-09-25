/**
 * Choix d'options d'un élève (`window.subjectOptions`, wwwroot/js/subject-options.js) — logique PURE,
 * partagée par l'écran d'inscription et l'onglet « Options » de la fiche élève : groupes d'un niveau,
 * exclusivité au sein d'un groupe, options cumulables, alerte sur les notes qu'un choix masquerait.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const so = () => loadScripts(['subject-options.js']).window.subjectOptions;

const SUBJECTS = [
    { id: 'maths', name: 'Maths', level: 'Collège', isOptional: false, optionGroup: null },
    { id: 'esp', name: 'Espagnol', level: 'Collège', isOptional: true, optionGroup: 'LV2' },
    { id: 'ara', name: 'Arabe', level: ' collège ', isOptional: true, optionGroup: ' lv2 ' },
    { id: 'pc', name: 'PC', level: 'Collège', isOptional: true, optionGroup: 'Option scientifique' },
    { id: 'des', name: 'Dessin', level: 'Collège', isOptional: true, optionGroup: null },
    { id: 'lat', name: 'Latin', level: 'Lycée', isOptional: true, optionGroup: 'LV2' }
];

test('les groupes d\'un niveau ignorent la casse, écartent les autres niveaux et les matières obligatoires', () => {
    const groups = plain(so().groupsForLevel(SUBJECTS, 'Collège'));

    assert.deepEqual(groups.map((g) => [g.label, g.exclusive, g.subjects.map((s) => s.id)]), [
        ['LV2', true, ['ara', 'esp']],
        ['Option scientifique', true, ['pc']],
        [null, false, ['des']]
    ]);
});

test('choisir dans un groupe exclusif remplace l\'autre choix du même groupe', () => {
    const lib = so();
    const groups = lib.groupsForLevel(SUBJECTS, 'Collège');
    const lv2 = groups[0];

    const first = lib.choose(lv2, [], 'esp');
    assert.deepEqual(plain(first), ['esp']);
    assert.deepEqual(plain(lib.choose(lv2, first, 'ara')), ['ara']);
    assert.deepEqual(plain(lib.choose(lv2, ['esp', 'pc'], 'ara')), ['pc', 'ara']);
});

test('une option sans groupe se coche et se décoche librement', () => {
    const lib = so();
    const dessin = lib.groupsForLevel(SUBJECTS, 'Collège')[2];

    const on = lib.choose(dessin, ['esp'], 'des');
    assert.deepEqual(plain(on), ['esp', 'des']);
    assert.deepEqual(plain(lib.choose(dessin, on, 'des')), ['esp']);
});

test('clearGroup retire le choix d\'un groupe sans toucher aux autres', () => {
    const lib = so();
    const lv2 = lib.groupsForLevel(SUBJECTS, 'Collège')[0];

    assert.deepEqual(plain(lib.clearGroup(lv2, ['esp', 'pc'])), ['pc']);
});

test('les groupes exclusifs sans choix sont signalés, pas les options libres', () => {
    const lib = so();
    const groups = lib.groupsForLevel(SUBJECTS, 'Collège');

    assert.deepEqual(plain(lib.unchosenGroupLabels(groups, ['esp'])), ['Option scientifique']);
    assert.deepEqual(plain(lib.unchosenGroupLabels(groups, ['esp', 'pc'])), []);
});

test('le nombre de notes masquées additionne les matières que le choix ne suit pas', () => {
    const lib = so();
    const groups = [
        { key: 'lv2', label: 'LV2', exclusive: true, subjects: [{ id: 'esp', name: 'Espagnol', gradeCount: 0 }, { id: 'ara', name: 'Arabe', gradeCount: 4 }] },
        { key: 'des', label: null, exclusive: false, subjects: [{ id: 'des', name: 'Dessin', gradeCount: 2 }] }
    ];

    assert.equal(lib.hiddenGradeCount(groups, ['esp', 'des']), 4);
    assert.equal(lib.hiddenGradeCount(groups, []), 6);
    assert.equal(lib.hiddenGradeCount(groups, ['ara', 'des']), 0);
});

const MANDATORY = [
    { subjectId: 'eps', name: 'EPS', isExempt: true, reason: 'Inaptitude médicale', gradeCount: 2 },
    { subjectId: 'maths', name: 'Mathématiques', isExempt: false, reason: null, gradeCount: 5 }
];

test('l\'état des dispenses reprend la case et le motif du serveur, motif vide pour une matière non dispensée', () => {
    assert.deepEqual(plain(so().exemptionStateFrom(MANDATORY)), {
        eps: { checked: true, reason: 'Inaptitude médicale' },
        maths: { checked: false, reason: '' }
    });
    assert.deepEqual(plain(so().exemptionStateFrom(undefined)), {});
});

test('le payload des dispenses ne contient que les matières cochées, motif nettoyé', () => {
    const lib = so();
    const state = { eps: { checked: true, reason: '  Certificat  ' }, maths: { checked: false, reason: 'ignoré' } };

    assert.deepEqual(plain(lib.exemptionsPayload(state)), [{ subjectId: 'eps', reason: 'Certificat' }]);
    assert.deepEqual(plain(lib.exemptionsPayload({})), [], 'aucune case cochée : une liste vide, pas « rien »');
});

test('une matière cochée sans motif est signalée par son nom ; décochée, elle ne l\'est pas', () => {
    const lib = so();

    assert.deepEqual(plain(lib.missingReasons(MANDATORY, { eps: { checked: true, reason: '   ' }, maths: { checked: false, reason: '' } })), ['EPS']);
    assert.deepEqual(plain(lib.missingReasons(MANDATORY, { eps: { checked: true, reason: 'ok' }, maths: { checked: false, reason: '' } })), []);
    assert.deepEqual(plain(lib.missingReasons(MANDATORY, { eps: { checked: false, reason: '' } })), []);
});

test('le décompte des notes masquées par les dispenses n\'additionne que les matières cochées', () => {
    const lib = so();

    assert.equal(lib.hiddenExemptionGrades(MANDATORY, { eps: { checked: true, reason: 'x' }, maths: { checked: true, reason: 'y' } }), 7);
    assert.equal(lib.hiddenExemptionGrades(MANDATORY, { eps: { checked: true, reason: 'x' } }), 2);
    assert.equal(lib.hiddenExemptionGrades(MANDATORY, {}), 0);
});
