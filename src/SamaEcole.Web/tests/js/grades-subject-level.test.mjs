/**
 * Écran de saisie des notes (`gradesView`) — les matières proposées suivent le NIVEAU de la classe choisie.
 *
 * « Mathématiques » existe au primaire, au collège et au lycée avec des coefficients différents : sans
 * filtre par niveau, la liste les mélangeait et l'utilisateur choisissait au hasard.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function gradesView() {
    const ctx = loadScripts(['pdf-preview.js', 'grades.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: { get: async () => [], toMessage: (_e, fallback) => fallback, toFieldErrors: (_e, fallback) => ({ global: fallback }) }
        }
    });
    const view = ctx.component('gradesView');
    view.classrooms = [
        { id: 'cm2', name: 'CM2 A', level: 'Primaire' },
        { id: '3e', name: '3e B', level: 'Collège' },
        { id: 'tle', name: 'Terminale S2', level: 'Lycée' },
        { id: 'autre', name: 'Daara', level: 'Niveau inconnu' }
    ];
    view.subjects = [
        { id: 'maths-p', name: 'Mathématiques', level: 'Primaire', sectionType: 'French' },
        { id: 'maths-c', name: 'Mathématiques', level: 'Collège', sectionType: 'French' },
        { id: 'maths-l', name: 'Mathématiques', level: 'Lycée', sectionType: 'French' },
        { id: 'philo', name: 'Philosophie', level: ' lycée ', sectionType: 'French' }
    ];
    return view;
}

test('sans classe choisie, toutes les matières sont proposées avec leur niveau dans le libellé', () => {
    const view = gradesView();

    assert.equal(view.visibleSubjects.length, 4);
    assert.equal(view.subjectLabel(view.subjects[0]), 'Mathématiques (Primaire)');
    assert.equal(view.subjectLabel(view.subjects[2]), 'Mathématiques (Lycée)');
});

test('une classe du collège ne propose que les matières du collège', () => {
    const view = gradesView();
    view.selectedClassroomId = '3e';

    assert.deepEqual(view.visibleSubjects.map(s => s.id), ['maths-c']);
    assert.equal(view.subjectLabel(view.subjects[1]), 'Mathématiques');
});

test('le rapprochement ignore la casse et les espaces', () => {
    const view = gradesView();
    view.selectedClassroomId = 'tle';

    assert.deepEqual(view.visibleSubjects.map(s => s.id), ['maths-l', 'philo']);
});

test('les synonymes et les accents sont reconnus comme sur l’écran Matières', () => {
    const view = gradesView();
    view.classrooms.push({ id: 'cem', name: '5e A', level: 'CEM' });
    view.subjects.push({ id: 'svt', name: 'SVT', level: 'College', sectionType: 'French' });
    view.selectedClassroomId = 'cem';

    assert.deepEqual(view.visibleSubjects.map(s => s.id), ['maths-c', 'svt']);
});

test('changer de classe efface la matière qui n’appartient plus au niveau', () => {
    const view = gradesView();
    view.selectedClassroomId = 'tle';
    view.selectedSubjectId = 'maths-l';

    view.selectedClassroomId = 'cm2';
    view.onClassroomChange();
    assert.equal(view.selectedSubjectId, '');
});

test('un niveau de classe sans aucune matière correspondante garde toute la liste', () => {
    const view = gradesView();
    view.selectedClassroomId = 'autre';

    assert.equal(view.visibleSubjects.length, 4);
});
