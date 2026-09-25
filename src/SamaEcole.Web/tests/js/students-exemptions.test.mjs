/**
 * Fiche élève › section « Dispenses » : les matières obligatoires que l'on peut dispenser pour l'année active, avec
 * un MOTIF obligatoire. La section est réservée à ceux qui gèrent l'élève, reste masquée sans erreur quand le serveur
 * ne la sert pas (403, pas d'année active) et n'envoie jamais un formulaire que le serveur refuserait.
 * Le composant est instancié sans `init()` (pas de réseau au démarrage) : on pilote ses méthodes directement.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const dto = () => ({
    studentId: 's1',
    schoolYearId: 'y1',
    subjects: [
        { subjectId: 'eps', name: 'EPS', isExempt: false, reason: null, gradeCount: 2 },
        { subjectId: 'maths', name: 'Mathématiques', isExempt: true, reason: 'Inaptitude médicale', gradeCount: 5 }
    ]
});

function students({ role = 'Secretariat', get = async () => dto(), put = async () => ({}) } = {}) {
    const calls = { get: [], put: [] };
    const ctx = loadScripts(['subject-exemptions.js', 'students.js'], {
        preload: {
            auth: { role, canView: () => true },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async (url) => { calls.get.push(url); return get(url); },
                put: async (url, body) => { calls.put.push({ url, body }); return put(url, body); },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    const view = ctx.initAlpine().get('studentsView')();
    view.detailStudent = { id: 's1' };
    return { view, calls };
}

test('le chargement appelle la route des dispenses de l\'élève et pré-remplit case et motif', async () => {
    const { view, calls } = students();

    await view.loadStudentExemptions('s1');

    assert.deepEqual(calls.get, ['/class-subjects/students/s1/exemptions']);
    assert.deepEqual(plain(view.exemptionState), {
        eps: { checked: false, reason: '' },
        maths: { checked: true, reason: 'Inaptitude médicale' }
    });
    assert.equal(view.exemptionsDirty, false);
});

test('cocher une dispense marque la section modifiée et compte les notes masquées', async () => {
    const { view } = students();
    await view.loadStudentExemptions('s1');
    assert.equal(view.hiddenExemptionGrades, 5, 'seule Mathématiques est cochée au départ');

    view.toggleExemption('eps');

    assert.equal(view.exemptionsDirty, true);
    assert.equal(view.hiddenExemptionGrades, 7, 'EPS (2) + Mathématiques (5)');
});

test('une dispense sans motif bloque l\'enregistrement et nomme la matière', async () => {
    const { view, calls } = students();
    await view.loadStudentExemptions('s1');

    view.toggleExemption('eps');

    assert.deepEqual(plain(view.missingExemptionReasons), ['EPS']);
    assert.equal(view.canSaveExemptions, false);

    await view.saveStudentExemptions();

    assert.equal(calls.put.length, 0, 'rien n\'est envoyé tant qu\'un motif manque');
    assert.match(view.exemptionsError, /EPS/);
});

test('l\'enregistrement envoie exactement les matières cochées, motif nettoyé, puis recharge', async () => {
    const { view, calls } = students();
    await view.loadStudentExemptions('s1');
    view.toggleExemption('eps');
    view.setExemptionReason('eps', '  Certificat  ');
    assert.equal(view.canSaveExemptions, true);

    await view.saveStudentExemptions();

    assert.equal(calls.put.length, 1);
    assert.equal(calls.put[0].url, '/class-subjects/students/s1/exemptions');
    assert.deepEqual(plain(calls.put[0].body), {
        exemptions: [
            { subjectId: 'eps', reason: 'Certificat' },
            { subjectId: 'maths', reason: 'Inaptitude médicale' }
        ]
    });
    assert.equal(calls.get.length, 2, 'la liste est rechargée après l\'enregistrement');
    assert.equal(view.exemptionsDirty, false);
    assert.ok(view.exemptionsNotice, 'un message de confirmation est affiché');
});

test('décocher toutes les dispenses envoie une liste vide, pas « rien »', async () => {
    const { view, calls } = students();
    await view.loadStudentExemptions('s1');

    view.toggleExemption('maths');
    await view.saveStudentExemptions();

    assert.deepEqual(plain(calls.put[0].body), { exemptions: [] });
});

test('sans le module Pédagogie (403) ou une classe sans matière dispensable, la section reste masquée', async () => {
    const refused = students({ get: async () => { throw new Error('403'); } });
    await refused.view.loadStudentExemptions('s1');
    assert.deepEqual(plain(refused.view.studentExemptions), []);
    assert.equal(refused.view.exemptionsError, null, 'aucune erreur affichée pour une section simplement indisponible');

    const empty = students({ get: async () => ({ studentId: 's1', schoolYearId: 'y1', subjects: [] }) });
    await empty.view.loadStudentExemptions('s1');
    assert.deepEqual(plain(empty.view.studentExemptions), []);
    assert.equal(empty.view.exemptionsError, null);
});

test('un rôle sans droit de gestion ne charge rien', async () => {
    const { view, calls } = students({ role: 'Enseignant' });

    await view.loadStudentExemptions('s1');

    assert.equal(calls.get.length, 0);
    assert.deepEqual(plain(view.studentExemptions), []);
});

test('une fiche changée entre-temps n\'écrase pas l\'état de l\'élève suivant', async () => {
    let release;
    const { view } = students({ get: () => new Promise((resolve) => { release = () => resolve(dto()); }) });

    const pending = view.loadStudentExemptions('s1');
    view.detailStudent = { id: 's2' };   // l'utilisateur a ouvert un autre élève
    release();
    await pending;

    assert.deepEqual(plain(view.studentExemptions), [], 'la réponse de s1 est ignorée');
    assert.deepEqual(plain(view.exemptionState), {});
});
