/**
 * `window.api.toFieldErrors` ne doit laisser AUCUNE erreur serveur muette (wwwroot/js/api.js).
 *
 * Cas réel : à l'inscription, le serveur répondait 422 `GuardianPhone` (numéro non sénégalais valide) alors que l'étape 3
 * du formulaire n'avait aucun `<p x-text="formErrors.guardianphone">`. L'erreur était reçue, le formulaire revenait à
 * l'étape 3 sans un mot, et le directeur voyait « Inscrire l'élève » sans aucun effet. Le même défaut guettait les
 * 30 écrans qui passent par toFieldErrors : le filet est donc central, pas écran par écran.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

/** Page factice dont les éléments `x-text` lisent les expressions données. */
function pageShowing(...xTexts) {
    const elements = xTexts.map((expr) => ({ getAttribute: (name) => (name === 'x-text' ? expr : null) }));
    return { querySelectorAll: (selector) => (selector === '[x-text]' ? elements : []) };
}

function api(document) {
    const ctx = loadScripts(['api.js'], { document });
    return ctx.window.api;
}

const validation = (details) => ({ status: 422, code: 'VALIDATION_ERROR', message: 'Une ou plusieurs erreurs…', details });
const PHONE = 'Le numéro de téléphone doit être un numéro sénégalais valide (ex: 77 123 45 67).';

test('une clé affichée par la page reste sous son champ, rien n\'est ajouté au bandeau', () => {
    const errors = api(pageShowing('formErrors.fullname', 'formErrors.global'))
        .toFieldErrors(validation({ FullName: ['Requis.'] }), 'x');

    assert.deepEqual({ ...errors }, { fullname: 'Requis.' });
});

test('une clé SANS emplacement (GuardianPhone) est reversée dans le bandeau global, jamais muette', () => {
    const errors = api(pageShowing('formErrors.fullname', 'formErrors.global'))
        .toFieldErrors(validation({ GuardianPhone: [PHONE] }), 'x');

    assert.equal(errors.global, PHONE);
    assert.equal(errors.guardianphone, PHONE, 'la clé d\'origine reste lisible par le code de l\'écran');
});

test('les messages orphelins s\'ajoutent à un message global existant, sans doublon des clés affichées', () => {
    const errors = api(pageShowing('formErrors.fullname', 'formErrors.global'))
        .toFieldErrors(validation({ FullName: ['Requis.'], RowVersion: ['Jeton requis.'], Autre: ['Autre.'] }), 'x');

    assert.equal(errors.global, 'Jeton requis. Autre.');
    assert.equal(errors.fullname, 'Requis.');
});

test('la notation crochets errors[\'email\'] et les variables nommées librement sont reconnues', () => {
    const errors = api(pageShowing("createItemErrors['label']", 'passwordErrors.confirm'))
        .toFieldErrors(validation({ Label: ['L.'], Confirm: ['C.'] }), 'x');

    assert.equal(errors.global, undefined);
});

test('un alias rabat une clé serveur sur le champ que l\'écran affiche (BedId → roomid)', () => {
    const errors = api(pageShowing('formErrors.roomid', 'formErrors.global'))
        .toFieldErrors(validation({ BedId: ['Ce pavillon accueille uniquement des filles.'] }), 'x', { bedid: 'roomid' });

    assert.equal(errors.roomid, 'Ce pavillon accueille uniquement des filles.');
    assert.equal(errors.global, undefined);
});

test('une page sans aucun affichage d\'erreur par champ n\'est pas modifiée (rien à comparer)', () => {
    const errors = api(pageShowing('title', 'student.fullName'))
        .toFieldErrors(validation({ GuardianPhone: [PHONE] }), 'x');

    assert.deepEqual({ ...errors }, { guardianphone: PHONE });
});

test('sans détail par champ : message global inchangé (409, règle métier, réseau)', () => {
    const errors = api(pageShowing('formErrors.global'))
        .toFieldErrors({ status: 409, message: 'Conflit.' }, 'repli');

    assert.deepEqual({ ...errors }, { global: 'Conflit.' });
});
