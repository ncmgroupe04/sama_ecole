/**
 * Écran de saisie des notes (`gradesView`, wwwroot/js/grades.js) — Évolution N°1.
 *
 * Le Secrétariat peut désormais SAISIR ; la correction d'une note existante n'est plus décidée par le
 * rôle mais par le SERVEUR, cellule par cellule (champ canEdit : fenêtre de correction de l'école,
 * auteur ou affectation pour l'Enseignant). Ce fichier verrouille ce contrat côté écran : on ne
 * recalcule jamais la règle, on suit le champ.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

function gradesView(role) {
    const ctx = loadScripts(['pdf-preview.js', 'grades.js'], {
        preload: {
            auth: { role },
            api: { get: async () => [], toMessage: (_e, fallback) => fallback, toFieldErrors: (_e, fallback) => ({ global: fallback }) }
        }
    });
    return ctx.component('gradesView');
}

test('Directeur, Secrétariat et Enseignant saisissent une nouvelle note', () => {
    for (const role of ['Directeur', 'Secretariat', 'Enseignant']) {
        assert.equal(gradesView(role).canEnterGrades, true, role);
    }
});

test('un rôle hors saisie ne saisit pas', () => {
    assert.equal(gradesView('Finance').canEnterGrades, false);
});

test('seul l\'Enseignant voit la note sur le délai de correction', () => {
    assert.equal(gradesView('Enseignant').isTeacher, true);
    assert.equal(gradesView('Directeur').isTeacher, false);
    assert.equal(gradesView('Secretariat').isTeacher, false);
});

test('une note existante suit le canEdit du serveur, sans le recalculer', () => {
    const view = gradesView('Enseignant');

    const editable = plain(view.toCell({ id: 'g1', value: 12, rowVersion: 5, canEdit: true }));
    const locked = plain(view.toCell({ id: 'g2', value: 9, rowVersion: 6, canEdit: false }));

    assert.equal(editable.canEdit, true);
    assert.equal(locked.canEdit, false, 'hors fenêtre ou note d\'un collègue : cellule grisée');
    assert.equal(locked.value, 9);
});

test('une cellule vide se saisit toujours : la fenêtre ne s\'applique qu\'à la correction', () => {
    const empty = plain(gradesView('Enseignant').toCell(null));

    assert.equal(empty.id, null);
    assert.equal(empty.canEdit, true);
});

test('l\'infobulle d\'une cellule verrouillée renvoie vers le Directeur ou le Secrétariat', () => {
    const hint = gradesView('Enseignant').lockedHint;

    assert.match(hint, /Directeur ou au Secrétariat/);
    assert.match(hint, /délai/);
});
