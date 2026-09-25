/**
 * Dispenses d'une matière obligatoire (`window.subjectExemptions`, wwwroot/js/subject-exemptions.js) — logique PURE
 * de la section « Dispenses » de la fiche élève : état du formulaire repris du serveur, charge utile envoyée (matières
 * cochées seulement, motif nettoyé), motifs manquants et notes qu'une dispense masquerait.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const se = () => loadScripts(['subject-exemptions.js']).window.subjectExemptions;

const MANDATORY = [
    { subjectId: 'eps', name: 'EPS', isExempt: true, reason: 'Inaptitude médicale', gradeCount: 2 },
    { subjectId: 'maths', name: 'Mathématiques', isExempt: false, reason: null, gradeCount: 5 }
];

test('l\'état des dispenses reprend la case et le motif du serveur, motif vide pour une matière non dispensée', () => {
    assert.deepEqual(plain(se().stateFrom(MANDATORY)), {
        eps: { checked: true, reason: 'Inaptitude médicale' },
        maths: { checked: false, reason: '' }
    });
    assert.deepEqual(plain(se().stateFrom(undefined)), {});
});

test('le payload des dispenses ne contient que les matières cochées, motif nettoyé', () => {
    const lib = se();
    const state = { eps: { checked: true, reason: '  Certificat  ' }, maths: { checked: false, reason: 'ignoré' } };

    assert.deepEqual(plain(lib.payload(state)), [{ subjectId: 'eps', reason: 'Certificat' }]);
    assert.deepEqual(plain(lib.payload({})), [], 'aucune case cochée : une liste vide, pas « rien »');
});

test('une matière cochée sans motif est signalée par son nom ; décochée, elle ne l\'est pas', () => {
    const lib = se();

    assert.deepEqual(plain(lib.missingReasons(MANDATORY, { eps: { checked: true, reason: '   ' }, maths: { checked: false, reason: '' } })), ['EPS']);
    assert.deepEqual(plain(lib.missingReasons(MANDATORY, { eps: { checked: true, reason: 'ok' }, maths: { checked: false, reason: '' } })), []);
    assert.deepEqual(plain(lib.missingReasons(MANDATORY, { eps: { checked: false, reason: '' } })), []);
});

test('le décompte des notes masquées par les dispenses n\'additionne que les matières cochées', () => {
    const lib = se();

    assert.equal(lib.hiddenGrades(MANDATORY, { eps: { checked: true, reason: 'x' }, maths: { checked: true, reason: 'y' } }), 7);
    assert.equal(lib.hiddenGrades(MANDATORY, { eps: { checked: true, reason: 'x' } }), 2);
    assert.equal(lib.hiddenGrades(MANDATORY, {}), 0);
});
