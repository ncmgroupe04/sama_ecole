/**
 * Fiche élève › onglet « Options & dispenses » : cible l'inscription de l'année active, n'invente pas de choix
 * quand aucun n'est enregistré, et traite les DEUX moitiés indépendamment — le choix d'options et les dispenses
 * de matières obligatoires (motif obligatoire). Une moitié non modifiée n'est jamais renvoyée au serveur :
 * enregistrer une dispense d'EPS ne doit pas écrire un choix d'options que personne n'a fait.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const HISTORY = [
    { enrollmentId: 'e-old', schoolYearId: 'y0', schoolYearLabel: '2025-2026', status: 'Confirmed', isActiveYear: false },
    { enrollmentId: 'e-now', schoolYearId: 'y1', schoolYearLabel: '2026-2027', status: 'Confirmed', isActiveYear: true }
];

const options = ({ explicit = false, arabeFollowed = true, epsExempt = false } = {}) => ({
    enrollmentId: 'e-now',
    hasExplicitChoice: explicit,
    groups: [{
        group: 'LV2',
        subjects: [
            { subjectId: 'esp', name: 'Espagnol', isFollowed: true, gradeCount: 0 },
            { subjectId: 'ara', name: 'Arabe', isFollowed: arabeFollowed, gradeCount: 3 }
        ]
    }],
    mandatorySubjects: [
        { subjectId: 'eps', name: 'EPS', isExempt: epsExempt, reason: epsExempt ? 'Inaptitude médicale' : null, gradeCount: 2 },
        { subjectId: 'maths', name: 'Mathématiques', isExempt: false, reason: null, gradeCount: 5 }
    ]
});

function students({ role = 'Secretariat', dto = options(), history = HISTORY } = {}) {
    const calls = { get: [], put: [] };
    const ctx = loadScripts(['subject-options.js', 'students.js'], {
        preload: {
            auth: { role, canView: () => true },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async (url) => { calls.get.push(url); return dto; },
                put: async (url, body) => { calls.put.push({ url, body }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    const view = ctx.initAlpine().get('studentsView')();
    view.studentDetail = { academicHistory: history };
    return { view, calls };
}

test('l\'onglet cible l\'inscription de l\'année active, jamais une ancienne ni une annulée', () => {
    assert.equal(students().view.activeEnrollmentEntry.enrollmentId, 'e-now');

    const cancelled = HISTORY.map((e) => (e.isActiveYear ? { ...e, status: 'Cancelled' } : e));
    assert.equal(students({ history: cancelled }).view.activeEnrollmentEntry, undefined);
});

test('sans choix enregistré, la sélection démarre vide et rien n\'est marqué modifié', async () => {
    const { view, calls } = students({ dto: options({ explicit: false }) });

    await view.openOptionsTab();

    assert.deepEqual(calls.get, ['/enrollments/e-now/options']);
    assert.equal(view.detailTab, 'options');
    assert.equal(view.optionsPanel.hasExplicitChoice, false);
    assert.deepEqual(plain(view.optionsPanel.selected), []);
    assert.equal(view.optionsPanel.optionsDirty, false);
    assert.equal(view.optionsPanel.exemptionsDirty, false);
    assert.equal(view.canSaveOptions, false);
});

test('avec un choix enregistré, la sélection reprend les matières suivies', async () => {
    const { view } = students({ dto: options({ explicit: true, arabeFollowed: false }) });

    await view.openOptionsTab();

    assert.deepEqual(plain(view.optionsPanel.selected), ['esp']);
});

test('les dispenses enregistrées pré-remplissent la case et le motif', async () => {
    const { view } = students({ dto: options({ epsExempt: true }) });

    await view.openOptionsTab();

    assert.deepEqual(plain(view.optionsPanel.exemptions), {
        eps: { checked: true, reason: 'Inaptitude médicale' },
        maths: { checked: false, reason: '' }
    });
});

test('choisir une option ne marque QUE les options comme modifiées et compte les notes masquées', async () => {
    const { view } = students();
    await view.openOptionsTab();

    view.chooseStudentOption(view.optionsPanel.groups[0], 'esp');

    assert.deepEqual(plain(view.optionsPanel.selected), ['esp']);
    assert.equal(view.optionsPanel.optionsDirty, true);
    assert.equal(view.optionsPanel.exemptionsDirty, false);
    assert.equal(view.hiddenOptionGrades, 3, 'l\'Arabe (3 notes) n\'est plus suivi');
    assert.deepEqual(plain(view.unchosenOptionGroups), []);
});

test('cocher une dispense ne marque QUE les dispenses comme modifiées ; le décompte ne compte pas les options intactes', async () => {
    const { view } = students();
    await view.openOptionsTab();

    view.toggleExemption('eps');

    assert.equal(view.optionsPanel.exemptions.eps.checked, true);
    assert.equal(view.optionsPanel.exemptionsDirty, true);
    assert.equal(view.optionsPanel.optionsDirty, false);
    assert.equal(view.hiddenOptionGrades, 2, 'seules les 2 notes d\'EPS : les options n\'ont pas été touchées');
});

test('une dispense sans motif bloque l\'enregistrement et nomme la matière', async () => {
    const { view, calls } = students();
    await view.openOptionsTab();
    view.toggleExemption('eps');

    assert.deepEqual(plain(view.missingExemptionReasons), ['EPS']);
    assert.equal(view.canSaveOptions, false);

    await view.saveOptions();

    assert.equal(calls.put.length, 0, 'rien n\'est envoyé sans motif');
    assert.match(view.optionsPanel.error, /EPS/);
});

test('enregistrer une dispense n\'envoie PAS le choix d\'options', async () => {
    const { view, calls } = students();
    await view.openOptionsTab();
    view.toggleExemption('eps');
    view.setExemptionReason('eps', '  Inaptitude médicale  ');

    await view.saveOptions();

    assert.deepEqual(plain(calls.put), [{
        url: '/enrollments/e-now/options',
        body: { exemptions: [{ subjectId: 'eps', reason: 'Inaptitude médicale' }] }
    }]);
    assert.equal('subjectIds' in calls.put[0].body, false);
});

test('enregistrer un choix d\'options n\'envoie PAS les dispenses', async () => {
    const { view, calls } = students();
    await view.openOptionsTab();
    view.chooseStudentOption(view.optionsPanel.groups[0], 'esp');

    await view.saveOptions();

    assert.deepEqual(plain(calls.put), [{ url: '/enrollments/e-now/options', body: { subjectIds: ['esp'] } }]);
    assert.equal(calls.get.length, 2, 'la fiche recharge l\'état serveur après l\'écriture');
    assert.equal(view.optionsPanel.optionsDirty, false);
    assert.equal(view.optionsPanel.saved, true);
});

test('décocher toutes les dispenses envoie une liste vide, pas « rien »', async () => {
    const { view, calls } = students({ dto: options({ epsExempt: true }) });
    await view.openOptionsTab();
    view.toggleExemption('eps');

    await view.saveOptions();

    assert.deepEqual(plain(calls.put[0].body), { exemptions: [] });
});

test('un rôle sans droit d\'écriture n\'a pas l\'onglet', () => {
    assert.equal(students({ role: 'Enseignant' }).view.canManageOptions, false);
    assert.equal(students({ role: 'Directeur' }).view.canManageOptions, true);
});
