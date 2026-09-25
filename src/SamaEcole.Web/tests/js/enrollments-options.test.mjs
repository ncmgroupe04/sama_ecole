/**
 * Écran d'inscription — bloc « Langues & options ». Le choix n'est transmis que si le secrétaire l'a
 * réellement composé POUR LA CLASSE CHOISIE : tant qu'il n'y touche pas — ou s'il change de classe après —
 * `optionSubjectIds` est OMIS et l'élève suit toutes les options (rien ne change pour une école sans option).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const CLASSROOMS = [
    { id: 'c-4a', name: '4ème A', level: 'Collège', cycle: 'College' },
    { id: 'c-ts2', name: 'Terminale S2', level: 'Lycée', cycle: 'Lycee' }
];
const SUBJECTS = [
    { id: 'esp', name: 'Espagnol', level: 'Collège', isOptional: true, optionGroup: 'LV2' },
    { id: 'ara', name: 'Arabe', level: 'Collège', isOptional: true, optionGroup: 'LV2' },
    { id: 'lat', name: 'Latin', level: 'Lycée', isOptional: true, optionGroup: 'LV2' },
    { id: 'maths', name: 'Maths', level: 'Collège', isOptional: false, optionGroup: null }
];

async function enrollments({ subjects = SUBJECTS, subjectsFail = false } = {}) {
    const posts = [];
    const ctx = loadScripts(['subject-options.js', 'enrollments.js'], {
        preload: {
            auth: { role: 'Secretariat' },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async (url) => {
                    if (url === '/subjects') {
                        if (subjectsFail) throw new Error('403');
                        return subjects;
                    }
                    return {
                        '/classrooms': CLASSROOMS,
                        '/school-years': [{ id: 'y1', label: '2026-2027', isActive: true }],
                        '/schools/current/settings': { tuitionMonthsPerYear: 9, isInternatEnabled: false }
                    }[url] ?? [];
                },
                post: async (url, body) => { posts.push({ url, body }); return { enrollmentId: 'e1' }; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });
    const view = ctx.initAlpine().get('enrollmentsView')();
    await view.loadReferenceData();
    return { view, posts };
}

test('les groupes proposés sont ceux du niveau de la classe choisie', async () => {
    const { view } = await enrollments();

    view.form.classroomId = 'c-4a';
    assert.deepEqual(plain(view.optionGroups).map((g) => [g.label, g.subjects.map((s) => s.id)]),
        [['LV2', ['ara', 'esp']]]);

    view.form.classroomId = 'c-ts2';
    assert.deepEqual(plain(view.optionGroups).map((g) => g.subjects.map((s) => s.id)), [['lat']]);
});

test('sans toucher au bloc, le choix est omis du payload', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';

    assert.deepEqual(plain(view.optionsPayload()), {});
});

test('un choix composé est transmis, exclusivité comprise', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';
    const lv2 = view.optionGroups[0];

    view.chooseOption(lv2, 'esp');
    view.chooseOption(lv2, 'ara');

    assert.deepEqual(plain(view.optionsPayload()), { optionSubjectIds: ['ara'] });
    assert.deepEqual(plain(view.unchosenOptionGroups), []);
});

test('changer de classe abandonne le choix : retour à « non renseigné », rien n\'est transmis', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';
    view.chooseOption(view.optionGroups[0], 'esp');

    view.form.classroomId = 'c-ts2';

    assert.equal(view.effectiveOptionSelection, null);
    assert.deepEqual(plain(view.optionsPayload()), {},
        'un choix composé pour une autre classe ne doit ni être envoyé ni dispenser des options du nouveau niveau');
});

test('vider un groupe rend explicite « aucune matière » et le signale', async () => {
    const { view } = await enrollments();
    view.form.classroomId = 'c-4a';
    const lv2 = view.optionGroups[0];

    view.clearOptionGroup(lv2);

    assert.deepEqual(plain(view.optionsPayload()), { optionSubjectIds: [] });
    assert.deepEqual(plain(view.unchosenOptionGroups), ['LV2']);
});

test('l\'inscription envoie le choix avec la commande', async () => {
    const { view, posts } = await enrollments();
    view.mode = 'NewEnrollment';
    view.form.classroomId = 'c-4a';
    view.form.fullName = 'Awa Ndiaye';
    view.chooseOption(view.optionGroups[0], 'esp');

    await view.submit();

    assert.equal(posts[0].url, '/enrollments');
    assert.deepEqual(plain(posts[0].body.optionSubjectIds), ['esp']);
});

test('une école sans option ne voit aucun bloc et n\'envoie rien', async () => {
    const { view, posts } = await enrollments({ subjects: [] });
    view.form.classroomId = 'c-4a';
    view.form.fullName = 'Awa Ndiaye';

    assert.equal(view.optionGroups.length, 0);
    await view.submit();

    assert.equal('optionSubjectIds' in posts[0].body, false);
});

test('sans le module Pédagogie (/subjects refusé), l\'écran reste utilisable et sans bloc', async () => {
    const { view } = await enrollments({ subjectsFail: true });

    assert.equal(view.error, null, 'le refus de /subjects ne doit pas bloquer l\'inscription');
    view.form.classroomId = 'c-4a';
    assert.equal(view.optionGroups.length, 0);
    assert.ok(view.classrooms.length > 0, 'les données de référence de l\'inscription sont chargées quand même');
});
