/**
 * Noms bilingues Élève & Tuteur, Tâche 7 — l'état du composant `studentsView` porte les deux miroirs
 * arabes facultatifs (fullNameAr, guardianNameAr) dans la modale de création, la modale d'édition et la
 * remise à zéro après enregistrement, et les transmet tels quels à l'API.
 *
 * Le rendu (dir="rtl", x-show, colonnes de l'aperçu d'import) se vérifie à l'écran ; ces tests verrouillent
 * la logique qui alimente ce rendu, la seule partie qui casse en silence (un champ oublié dans un objet
 * littéral est simplement jamais envoyé).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

function students() {
    const calls = [];
    const ctx = loadScripts(['students.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async () => ({ items: [], totalCount: 0 }),
                post: async (endpoint, body) => { calls.push({ method: 'POST', endpoint, body: plain(body) }); return { id: 'new-id', matricule: 'ELEV-1' }; },
                put: async (endpoint, body) => { calls.push({ method: 'PUT', endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/eleves', search: '' }
        }
    });
    const view = ctx.initAlpine().get('studentsView')();
    // On n'exerce ici que l'état des formulaires : le rechargement des listes est hors sujet.
    view.loadStudents = async () => {};
    view.refreshStudentDetail = async () => {};
    return { view, calls };
}

const IDENTITY = {
    id: 's-1', fullName: 'Awa Fall', birthDate: '2015-03-12', birthPlace: 'Dakar', gender: 'F',
    classroomId: 'c-1', photoUrl: null, guardianName: 'Moussa Fall', guardianPhone: null, guardianEmail: null,
    address: null, rowVersion: 7, fullNameAr: 'أوا فال', guardianNameAr: 'موسى فال'
};

test('création : le formulaire démarre avec les deux noms arabes vides', () => {
    const { view } = students();

    assert.equal(view.newStudent.fullNameAr, '');
    assert.equal(view.newStudent.guardianNameAr, '');
});

test('création : les noms arabes partent dans le POST, puis le formulaire est remis à zéro', async () => {
    const { view, calls } = students();
    view.newStudent.fullName = 'Awa Fall';
    view.newStudent.fullNameAr = 'أوا فال';
    view.newStudent.guardianNameAr = 'موسى فال';

    await view.submitCreate();

    assert.equal(calls.length, 1);
    assert.equal(calls[0].endpoint, '/students');
    assert.equal(calls[0].body.fullNameAr, 'أوا فال');
    assert.equal(calls[0].body.guardianNameAr, 'موسى فال');
    assert.equal(view.newStudent.fullNameAr, '', 'le nom arabe ne doit pas fuiter vers la fiche suivante');
    assert.equal(view.newStudent.guardianNameAr, '');
});

test('édition : la modale est pré-remplie avec les noms arabes de la fiche', () => {
    const { view } = students();
    view.studentDetail = { identity: IDENTITY };

    view.openEditStudent();

    assert.equal(view.editingStudent.fullNameAr, 'أوا فال');
    assert.equal(view.editingStudent.guardianNameAr, 'موسى فال');
});

test('édition : une fiche sans nom arabe donne des chaînes vides, jamais null ni undefined', () => {
    const { view } = students();
    view.studentDetail = { identity: { ...IDENTITY, fullNameAr: null, guardianNameAr: undefined } };

    view.openEditStudent();

    assert.equal(view.editingStudent.fullNameAr, '');
    assert.equal(view.editingStudent.guardianNameAr, '');
});

test('édition : les noms arabes modifiés partent dans le PUT', async () => {
    const { view, calls } = students();
    view.studentDetail = { identity: IDENTITY };
    view.detailStudent = { id: 's-1' };
    view.openEditStudent();
    view.editingStudent.fullNameAr = 'أوا ندياي';

    await view.submitEditStudent();

    assert.equal(calls.length, 1);
    assert.equal(calls[0].method, 'PUT');
    assert.equal(calls[0].endpoint, '/students/s-1');
    assert.equal(calls[0].body.fullNameAr, 'أوا ندياي');
    assert.equal(calls[0].body.guardianNameAr, 'موسى فال');
});

test('édition : fermer la modale remet les noms arabes à vide', () => {
    const { view } = students();
    view.studentDetail = { identity: IDENTITY };
    view.openEditStudent();

    view.closeEditStudent();

    assert.equal(view.editingStudent.fullNameAr, '');
    assert.equal(view.editingStudent.guardianNameAr, '');
});
