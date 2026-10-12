/**
 * Écran de saisie des notes (`gradesView`) — Franco-Arabe, Bloc 2 : grille RTL et filtre par pôle.
 *
 * Le SENS de lecture suit la matière sélectionnée (section arabe / sciences islamiques → RTL, tout le
 * reste → LTR), jamais une préférence globale : un même enseignant saisit parfois des matières des deux
 * sections. Le filtre de pôle ne fait que restreindre la liste des matières proposées.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function gradesView(subjects = []) {
    const ctx = loadScripts(['pdf-preview.js', 'grades.js'], {
        preload: {
            auth: { role: 'Enseignant' },
            api: { get: async () => [], toMessage: (_e, fallback) => fallback, toFieldErrors: (_e, fallback) => ({ global: fallback }) }
        }
    });
    const view = ctx.component('gradesView');
    view.subjects = subjects;
    return view;
}

const MATHS = { id: 'maths', name: 'Mathématiques', sectionType: 'French' };
const FIQH = { id: 'fiqh', name: 'Fiqh', nameAr: 'الفقه', sectionType: 'IslamicStudies' };
const ARABE = { id: 'arabe', name: 'Langue arabe', nameAr: 'اللغة العربية', sectionType: 'Arabic' };
const EPS = { id: 'eps', name: 'EPS', sectionType: 'Common' };

test('une matière française se saisit de gauche à droite, avec les en-têtes français', () => {
    const view = gradesView([MATHS, FIQH]);
    view.selectedSubjectId = 'maths';

    assert.equal(view.isRtlGrid, false);
    assert.equal(view.gridDir, 'ltr');
    assert.equal(view.gridLabels.devoir1, 'Devoir 1');
});

test('une matière de la section arabe bascule la grille en RTL avec les en-têtes arabes', () => {
    const view = gradesView([MATHS, ARABE]);
    view.selectedSubjectId = 'arabe';

    assert.equal(view.isRtlGrid, true);
    assert.equal(view.gridDir, 'rtl');
    assert.equal(view.gridLabels.devoir1, 'الفرض 1');
    assert.equal(view.gridLabels.composition, 'الامتحان');
});

test('les sciences islamiques se saisissent aussi en RTL', () => {
    const view = gradesView([FIQH]);
    view.selectedSubjectId = 'fiqh';

    assert.equal(view.isRtlGrid, true);
});

test('le pôle commun reste en LTR', () => {
    const view = gradesView([EPS]);
    view.selectedSubjectId = 'eps';

    assert.equal(view.isRtlGrid, false);
});

test('sans matière sélectionnée, ou avec un identifiant inconnu, la grille reste en LTR', () => {
    const view = gradesView([FIQH]);

    assert.equal(view.isRtlGrid, false);
    view.selectedSubjectId = 'inconnue';
    assert.equal(view.isRtlGrid, false);
});

test('une matière sans pôle (réponse d\'avant l\'option) est traitée comme française', () => {
    const view = gradesView([{ id: 'vieille', name: 'Histoire' }]);
    view.selectedSubjectId = 'vieille';

    assert.equal(view.isRtlGrid, false);
    assert.equal(view.hasPoles, false);
});

test('en RTL, le nom arabe de l\'élève est affiché, avec repli sur le français s\'il est absent', () => {
    const view = gradesView([FIQH]);
    view.selectedSubjectId = 'fiqh';

    assert.equal(view.studentLabel({ fullName: 'Moussa Diop', fullNameAr: 'موسى ديوب' }), 'موسى ديوب');
    assert.equal(view.studentLabel({ fullName: 'Awa Ndiaye', fullNameAr: null }), 'Awa Ndiaye');
});

test('en LTR, le nom français de l\'élève est toujours affiché', () => {
    const view = gradesView([MATHS]);
    view.selectedSubjectId = 'maths';

    assert.equal(view.studentLabel({ fullName: 'Moussa Diop', fullNameAr: 'موسى ديوب' }), 'Moussa Diop');
});

test('le filtre de pôle n\'apparaît que si l\'école a une matière hors section française', () => {
    assert.equal(gradesView([MATHS]).hasPoles, false);
    assert.equal(gradesView([MATHS, ARABE]).hasPoles, true);
});

test('sans filtre, toutes les matières notables sont proposées', () => {
    const view = gradesView([MATHS, FIQH, ARABE, EPS]);

    assert.deepEqual(view.visibleSubjects.map(s => s.id), ['maths', 'fiqh', 'arabe', 'eps']);
});

test('le pôle « Section arabe » regroupe langue arabe ET sciences islamiques', () => {
    const view = gradesView([MATHS, FIQH, ARABE, EPS]);
    view.selectedPole = 'Arabic';

    assert.deepEqual(view.visibleSubjects.map(s => s.id), ['fiqh', 'arabe']);
});

test('le pôle « Section française » exclut les matières arabes et communes', () => {
    const view = gradesView([MATHS, FIQH, ARABE, EPS]);
    view.selectedPole = 'French';

    assert.deepEqual(view.visibleSubjects.map(s => s.id), ['maths']);
});

test('changer de pôle désélectionne une matière qui n\'en fait plus partie', async () => {
    const view = gradesView([MATHS, FIQH]);
    view.selectedSubjectId = 'maths';
    view.selectedPole = 'Arabic';

    await view.onPoleChange();

    assert.equal(view.selectedSubjectId, '', 'jamais une matière française sélectionnée sous le pôle arabe');
});

test('changer de pôle garde la matière sélectionnée si elle appartient encore au pôle', async () => {
    const view = gradesView([MATHS, FIQH]);
    view.selectedSubjectId = 'fiqh';
    view.selectedPole = 'Arabic';

    await view.onPoleChange();

    assert.equal(view.selectedSubjectId, 'fiqh');
});

test('le libellé du sélecteur ajoute le nom arabe seulement quand l\'école a des pôles', () => {
    assert.equal(gradesView([MATHS, FIQH]).subjectLabel(FIQH), 'Fiqh — الفقه');
    assert.equal(gradesView([{ id: 'x', name: 'Fiqh', nameAr: 'الفقه' }]).subjectLabel({ id: 'x', name: 'Fiqh', nameAr: 'الفقه' }), 'Fiqh',
        'école sans aucune matière hors section française : le libellé d\'avant');
});

test('les domaines d\'une grille par compétences ne sont jamais proposés, quel que soit le pôle', () => {
    const domaine = { id: 'dom', name: 'Langue', sectionType: 'Arabic' };
    const activite = { id: 'act', name: 'Lecture', parentSubjectId: 'dom', sectionType: 'Arabic' };
    const view = gradesView([domaine, activite]);
    view.selectedPole = 'Arabic';

    assert.deepEqual(view.visibleSubjects.map(s => s.id), ['act']);
});
