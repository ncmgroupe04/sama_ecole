/**
 * Évolution N°6 — séries, matières et options, côté écran.
 *
 * 1. Onglet « Matières par classe » (`classSubjectsView`, wwwroot/js/class-subjects.js) : aucune règle métier,
 *    il envoie le coefficient par PUT /coefficients (portée classe, jeton xmin), l'activation et le groupe
 *    d'options par PUT /class-subjects/{id}, et n'offre l'écriture qu'au Directeur.
 * 2. Formulaire d'inscription (`enrollmentsView`) : la section « Matières optionnelles » suit la classe choisie,
 *    pré-coche l'option par défaut et transmet une option par groupe.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const CLASSROOMS = [
    { id: 'c-cm2', name: 'CM2 A', cycle: 'Primaire', series: null },
    { id: 'c-tl2', name: 'Terminale L2 A', cycle: 'Lycee', series: 'L2' }
];

const row = (overrides = {}) => ({
    id: 'cs-maths', subjectId: 's-maths', subjectName: 'Mathématiques', level: 'Lycée', optionGroup: null,
    isCustom: false, isActive: true, displayOrder: 0, rowVersion: 7, baseCoefficient: 4, effectiveCoefficient: 2,
    source: 'Classroom', overrideId: 'o-1', overrideCoefficient: 2, overrideRowVersion: 11, officialCoefficient: 2,
    enrolledStudents: null, ...overrides
});

const PROGRAMME = {
    classroomId: 'c-tl2', classroomName: 'Terminale L2 A', series: 'L2', seriesLabel: 'Série L2', hasTemplate: true,
    schoolYearId: 'y-1', schoolYearLabel: '2026-2027',
    rows: [
        row(),
        row({ id: 'cs-svt', subjectId: 's-svt', subjectName: 'SVT', optionGroup: 'Option scientifique', overrideId: null,
            overrideCoefficient: null, overrideRowVersion: null, effectiveCoefficient: 2, source: 'Subject', enrolledStudents: 12 }),
        row({ id: 'cs-pc', subjectId: 's-pc', subjectName: 'Physique-Chimie', optionGroup: 'Option scientifique', overrideId: null,
            overrideCoefficient: null, overrideRowVersion: null, effectiveCoefficient: 3, officialCoefficient: 2, source: 'Subject', enrolledStudents: 20 })
    ],
    optionGroups: [{ name: 'Option scientifique', defaultClassSubjectId: 'cs-pc', studentsWithoutChoice: 3 }]
};

async function classSubjects({ role = 'Directeur' } = {}) {
    const calls = [];
    const record = (method) => async (endpoint, body) => {
        calls.push({ method, endpoint, body: body === undefined ? undefined : plain(body) });
        if (endpoint === '/class-subjects/reset') return { coefficientsSet: 1, coefficientsCleared: 0, subjectsAdded: 0, subjectsRestored: 1 };
        if (endpoint === '/class-subjects/assign-default-options') return { studentsUpdated: 3, optionsAssigned: 3 };
        return {};
    };
    const ctx = loadScripts(['class-subjects.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/classrooms') return CLASSROOMS;
                    if (endpoint === '/subjects') return [];
                    if (endpoint.startsWith('/class-subjects?')) return PROGRAMME;
                    return [];
                },
                post: record('POST'),
                put: record('PUT'),
                delete: record('DELETE'),
                toMessage: (_e, fallback) => fallback
            }
        }
    });
    const view = ctx.component('classSubjectsView'); // component() exécute init()
    await flush();
    return { view, calls };
}

test('le programme de la première classe de série est chargé, le primaire n\'est pas proposé', async () => {
    const { view, calls } = await classSubjects();

    assert.ok(calls.some((c) => c.endpoint === '/class-subjects?classroomId=c-tl2'));
    assert.deepEqual(plain(view.classroomChoices.map((c) => c.value)), ['c-tl2']);
    assert.equal(view.rows.length, 3);
    assert.equal(view.studentsWithoutOption, 3);
    assert.equal(view.defaultOptionName(view.groups[0]), 'Physique-Chimie');
});

test('un coefficient s\'enregistre en surcharge de classe, avec le jeton xmin de la surcharge existante', async () => {
    const { view, calls } = await classSubjects();
    const maths = view.rows[0];

    maths.draft = '3,5';
    view.saveCoefficient(maths);
    await flush();

    const put = calls.find((c) => c.method === 'PUT' && c.endpoint === '/coefficients');
    assert.deepEqual(put.body, { subjectId: 's-maths', classroomId: 'c-tl2', coefficient: 3.5, rowVersion: 11 });
});

test('désactiver une matière garde son groupe et envoie le jeton xmin de la ligne', async () => {
    const { view, calls } = await classSubjects();

    view.toggleActive(view.rows[1]);
    await flush();

    const put = calls.find((c) => c.method === 'PUT' && c.endpoint === '/class-subjects/cs-svt');
    assert.deepEqual(put.body, { isActive: false, optionGroup: 'Option scientifique', rowVersion: 7 });
});

test('un écart à la valeur officielle est signalé', async () => {
    const { view } = await classSubjects();

    assert.equal(view.differsFromOfficial(view.rows[0]), false);
    assert.equal(view.differsFromOfficial(view.rows[2]), true);
});

test('le Secrétariat ne modifie rien mais peut affecter les options par défaut', async () => {
    const { view, calls } = await classSubjects({ role: 'Secretariat' });

    assert.equal(view.canEdit, false);
    assert.equal(view.canReset, false);
    assert.equal(view.canAssign, true);

    await view.assignDefaults();
    assert.ok(calls.some((c) => c.method === 'POST' && c.endpoint === '/class-subjects/assign-default-options'));
});

test('la réinitialisation vise la classe choisie', async () => {
    const { view, calls } = await classSubjects();

    await view.reset();

    const post = calls.find((c) => c.endpoint === '/class-subjects/reset');
    assert.deepEqual(post.body, { classroomId: 'c-tl2' });
    assert.match(view.notice, /Coefficients officiels rétablis/);
});

// ── Formulaire d'inscription : section « Matières optionnelles » ─────────────────────────────────────────

const OPTIONS = {
    classroomId: 'c-tl2', schoolYearId: 'y-1',
    groups: [
        { name: 'Option scientifique', defaultClassSubjectId: 'cs-pc', selectedClassSubjectId: null,
          options: [{ classSubjectId: 'cs-svt', subjectName: 'SVT' }, { classSubjectId: 'cs-pc', subjectName: 'Physique-Chimie' }] },
        { name: 'LV2', defaultClassSubjectId: 'cs-esp', selectedClassSubjectId: null,
          options: [{ classSubjectId: 'cs-esp', subjectName: 'Espagnol' }, { classSubjectId: 'cs-all', subjectName: 'Allemand' }] }
    ]
};

function enrollments({ optionsFail = false } = {}) {
    const calls = [];
    const ctx = loadScripts(['enrollments.js'], {
        preload: {
            auth: { role: 'Secretariat' },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint.startsWith('/class-subjects/options')) {
                        if (optionsFail) throw Object.assign(new Error('403'), { status: 403 });
                        return OPTIONS;
                    }
                    return [];
                },
                post: async (endpoint, body) => { calls.push({ method: 'POST', endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/inscriptions', search: '' }
        }
    });
    // Sans init() : le harnais ne fournit pas $watch — on exerce directement la section « Matières optionnelles ».
    return { view: ctx.initAlpine().get('enrollmentsView')(), calls };
}

test('inscription : l\'option par défaut de chaque groupe est pré-cochée, un choix déjà fait est conservé', async () => {
    const { view } = enrollments();
    view.form.classroomId = 'c-tl2';
    view.form.subjectOptions = { 'Option scientifique': 'cs-svt' };

    await view.loadSubjectOptions('c-tl2');

    assert.equal(view.optionGroups.length, 2);
    assert.deepEqual(plain(view.form.subjectOptions), { 'Option scientifique': 'cs-svt', LV2: 'cs-esp' });
    assert.deepEqual(plain(view.subjectOptionsPayload()), { subjectOptionIds: ['cs-svt', 'cs-esp'] });
});

test('inscription : sans groupe d\'options (ou sans module Pédagogie), rien n\'est transmis', async () => {
    const { view } = enrollments({ optionsFail: true });
    view.form.classroomId = 'c-tl2';

    await view.loadSubjectOptions('c-tl2');

    assert.equal(view.optionGroups.length, 0);
    assert.deepEqual(plain(view.subjectOptionsPayload()), {});
});
