/**
 * Sélecteur de date maison (`dateField`, wwwroot/js/ui-components.js + TagHelpers/DateFieldTagHelper.cs).
 *
 * Ce composant remplace TOUS les <input type="date"> du produit : un bug ici se paie sur la date de
 * naissance d'un élève, la date d'un paiement, celle d'une sanction, d'un prêt de matériel… Les cas
 * couverts ici sont ceux qui produisent une valeur FAUSSE sans rien signaler à l'écran — la pire
 * catégorie, puisque l'utilisateur n'a aucune raison de se méfier de ce qu'il lit.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

/** Instancie le composant Alpine `dateField` hors navigateur. */
function dateField(initialIso) {
    const ctx = loadScripts(['ui-components.js']);
    const components = ctx.initAlpine();
    const instance = components.get('dateField')(initialIso);
    if (typeof instance.init === 'function') instance.init();
    return instance;
}

test('formatInput rend JJ/MM/AAAA pour une date simple', () => {
    const field = dateField('2024-03-07');
    assert.equal(field.formatInput('2024-03-07'), '07/03/2024');
});

test('formatInput ne doit pas produire de charabia sur un ISO horodaté', () => {
    const field = dateField();
    // Beaucoup de champs de l'API sont des DateTimeOffset : ils arrivent avec l'heure collée.
    // Sans troncature, `split('-')` rend "07T00:00:00+00:00" comme jour.
    assert.equal(field.formatInput('2024-03-07T00:00:00+00:00'), '07/03/2024');
});

test('formatInput ne jette jamais sur une valeur absente', () => {
    const field = dateField();
    assert.doesNotThrow(() => field.formatInput(null));
    assert.doesNotThrow(() => field.formatInput(undefined));
    assert.doesNotThrow(() => field.formatInput(''));
    assert.equal(field.formatInput(null), '');
});

test('formatDisplay n\'affiche jamais « Invalid Date »', () => {
    const field = dateField();
    assert.equal(field.formatDisplay(null), '');
    assert.equal(field.formatDisplay(''), '');
    assert.ok(!String(field.formatDisplay('pas-une-date')).includes('Invalid'));
});

test('parseInput accepte une date collée au format J/M/AAAA', () => {
    const field = dateField();
    // Coller « 1/1/2024 » est un geste courant ; la saisie au clavier, elle, est déjà normalisée
    // par onTextInput. Sans tolérance, le collage produit une date fausse ou rien du tout.
    assert.equal(field.parseInput('1/1/2024'), '2024-01-01');
    assert.equal(field.parseInput('07/03/2024'), '2024-03-07');
});

test('parseInput refuse un débordement de calendrier', () => {
    const field = dateField();
    assert.equal(field.parseInput('31/02/2024'), null, '31 février n\'existe pas');
    assert.equal(field.parseInput('32/01/2024'), null);
    assert.equal(field.parseInput('01/13/2024'), null);
    assert.equal(field.parseInput('01/01/20'), null, 'année sur 2 chiffres : ambigu, on refuse');
});

test('onTextInput insère les séparateurs au fil de la frappe', () => {
    const field = dateField();
    field.text = '07032024';
    field.onTextInput();
    assert.equal(field.text, '07/03/2024');
    assert.equal(field.pendingIso, '2024-03-07');
});

test('onTextInput laisse pendingIso à null tant que la date est incomplète', () => {
    const field = dateField();
    field.text = '0703';
    field.onTextInput();
    assert.equal(field.text, '07/03');
    assert.equal(field.pendingIso, null, 'une date incomplète ne doit JAMAIS être écrite dans le modèle');
});

test('la grille du mois commence un lundi et couvre le mois entier', () => {
    const field = dateField('2024-03-07');
    const days = field.days;
    assert.equal(days.length, 42);
    // 1er mars 2024 = vendredi → la grille démarre le lundi 26 février.
    assert.equal(days[0].iso, '2024-02-26');
    assert.equal(days[0].currentMonth, false);
    const inMonth = days.filter((d) => d.currentMonth);
    assert.equal(inMonth.length, 31, 'mars compte 31 jours');
    assert.equal(inMonth[0].iso, '2024-03-01');
    assert.equal(inMonth[30].iso, '2024-03-31');
});

test('toIso ne décale pas la date sur un fuseau à l\'ouest de Greenwich', () => {
    const field = dateField();
    // toISOString() partirait en UTC et rendrait la veille pour tout fuseau négatif : le composant
    // doit composer la chaîne à partir des composantes LOCALES.
    assert.equal(field.toIso(new Date(2024, 0, 1)), '2024-01-01');
    assert.equal(field.toIso(new Date(2024, 11, 31)), '2024-12-31');
});

test('la navigation par chevrons ne sort pas de la plage d\'années proposée', () => {
    // Le <select> Année du Tag Helper n'offre que [année-100 ; année+5]. Si prevMonth() emmène
    // viewYear hors de cette plage, le <select> n'a plus d'option correspondante : le navigateur
    // retombe sur la première et x-model réécrit viewYear — le calendrier saute d'un siècle.
    const currentYear = new Date().getFullYear();
    const field = dateField(`${currentYear - 100}-01-15`);

    field.prevMonth();

    assert.ok(
        field.viewYear >= currentYear - 100,
        `viewYear=${field.viewYear} sort de la plage du sélecteur (min ${currentYear - 100})`
    );
});

test('la navigation par chevrons ne dépasse pas la borne haute du sélecteur', () => {
    const currentYear = new Date().getFullYear();
    const field = dateField(`${currentYear + 5}-12-15`);

    field.nextMonth();

    assert.ok(
        field.viewYear <= currentYear + 5,
        `viewYear=${field.viewYear} sort de la plage du sélecteur (max ${currentYear + 5})`
    );
});
