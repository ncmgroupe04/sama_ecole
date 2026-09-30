/**
 * Frais optionnels, Tâche 2 — cases à cocher « Uniforme », « Tenue de sport »… du formulaire d'inscription
 * ET de réinscription (un seul composant `enrollmentsView` sert les deux modes).
 *
 * Ce que ces tests verrouillent :
 *   1. les frais optionnels sont PRÉ-COCHÉS à l'affichage ; les obligatoires n'ont pas de case ;
 *   2. décocher un frais le sort du total affiché, sans le faire disparaître de la liste (on doit pouvoir
 *      le recocher) ;
 *   3. le payload transmet la liste EXPLICITE des frais optionnels cochés (vide = aucun), dans les deux modes ;
 *   4. l'aperçu se recompose comme le serveur (OptionalFeeSelection) — le serveur reste l'autorité.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

const CLASS = 'c-6e';
const INSCRIPTION = 'cat-inscription';
const MENSUALITE = 'cat-mensualite';
const UNIFORME = 'cat-uniforme';
const TENUE = 'cat-tenue';

function enrollments({ apiGet } = {}) {
    const posts = [];
    const ctx = loadScripts(['enrollments.js'], {
        preload: {
            auth: { role: 'Secretariat' },
            pdfPreview: { state: () => ({}) },
            api: {
                get: apiGet || (async () => []),
                post: async (endpoint, body) => { posts.push({ endpoint, body: plain(body) }); return {}; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            location: { href: 'https://localhost/inscriptions', search: '' }
        }
    });
    const view = ctx.initAlpine().get('enrollmentsView')();
    return { view, posts };
}

/** Barème de la 6e : deux frais obligatoires, deux optionnels. */
function withFees(view) {
    view.tuitionMonths = 10;
    view.form.classroomId = CLASS;
    view.feesByClassroom = {
        [CLASS]: [
            { feeCategoryId: INSCRIPTION, designation: 'Inscription', isRecurring: false, isOptional: false, unitAmount: 10000 },
            { feeCategoryId: MENSUALITE, designation: 'Mensualité', isRecurring: true, isOptional: false, unitAmount: 15000 },
            { feeCategoryId: UNIFORME, designation: 'Uniforme', isRecurring: false, isOptional: true, unitAmount: 25000 },
            { feeCategoryId: TENUE, designation: 'Tenue de sport', isRecurring: false, isOptional: true, unitAmount: 8000 }
        ]
    };
}

const MANDATORY_TOTAL = 10000 + 15000 * 10;

test('les frais optionnels sont pré-cochés à l\'affichage du formulaire', () => {
    const { view } = enrollments();
    withFees(view);

    assert.equal(view.isFeeChecked(UNIFORME), true);
    assert.equal(view.isFeeChecked(TENUE), true);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 25000 + 8000);
});

test('feeChoices liste tous les frais de la classe et signale les optionnels', () => {
    const { view } = enrollments();
    withFees(view);

    const choices = plain(view.feeChoices());
    assert.equal(choices.length, 4);
    assert.deepEqual(
        choices.filter((c) => c.isOptional).map((c) => c.feeCategoryId).sort(),
        [TENUE, UNIFORME].sort());
    assert.ok(choices.every((c) => c.checked), 'tout est coché par défaut');
});

test('décocher un frais optionnel le sort du total mais le garde dans la liste', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(UNIFORME);

    assert.equal(view.isFeeChecked(UNIFORME), false);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 8000);
    assert.ok(!view.previewLines().some((l) => l.feeCategoryId === UNIFORME));
    const uniforme = plain(view.feeChoices()).find((c) => c.feeCategoryId === UNIFORME);
    assert.equal(uniforme.checked, false, 'la ligne reste affichée pour pouvoir être recochée');
});

test('recocher un frais le remet dans le total', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(UNIFORME);
    view.toggleFee(UNIFORME);

    assert.equal(view.isFeeChecked(UNIFORME), true);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 25000 + 8000);
});

test('un frais obligatoire ne se décoche pas', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(MENSUALITE);
    view.toggleFee(INSCRIPTION);

    assert.equal(view.isFeeChecked(MENSUALITE), true);
    assert.equal(view.isFeeChecked(INSCRIPTION), true);
    assert.equal(view.previewTotal(), MANDATORY_TOTAL + 25000 + 8000);
});

test('le simulateur d\'aide au calcul ne compte pas un frais décoché', () => {
    const { view } = enrollments();
    withFees(view);
    view.simMonths = 2;

    view.toggleFee(TENUE);

    // ponctuels facturés (inscription + uniforme) + 2 mensualités
    assert.equal(view.simSubtotal(), 10000 + 25000 + 15000 * 2);
});

test('le payload transmet les frais optionnels cochés', () => {
    const { view } = enrollments();
    withFees(view);

    assert.deepEqual(plain(view.optionalFeePayload()).optionalFeeCategoryIds.sort(), [TENUE, UNIFORME].sort());

    view.toggleFee(UNIFORME);
    assert.deepEqual(plain(view.optionalFeePayload()), { optionalFeeCategoryIds: [TENUE] });
});

test('tout décocher transmet une liste VIDE (aucun frais optionnel), jamais un champ absent', () => {
    const { view } = enrollments();
    withFees(view);

    view.toggleFee(UNIFORME);
    view.toggleFee(TENUE);

    assert.deepEqual(plain(view.optionalFeePayload()), { optionalFeeCategoryIds: [] });
});

test('une classe sans frais optionnel ne transmet rien', () => {
    const { view } = enrollments();
    view.form.classroomId = 'c-sans-options';
    view.feesByClassroom = {
        'c-sans-options': [
            { feeCategoryId: INSCRIPTION, designation: 'Inscription', isRecurring: false, isOptional: false, unitAmount: 10000 }
        ]
    };

    assert.deepEqual(plain(view.optionalFeePayload()), {});
});

test('changer de classe recoche les frais optionnels', () => {
    const { view } = enrollments();
    withFees(view);
    view.toggleFee(UNIFORME);

    view.resetOptionalFees();

    assert.equal(view.isFeeChecked(UNIFORME), true);
});

test('réinscription : le payload envoyé au serveur porte les frais cochés', async () => {
    const { view, posts } = enrollments();
    withFees(view);
    view.mode = 'ReEnrollment';
    view.activeYear = { id: 'y1', isActive: true };
    view.form.studentId = 'stu-1';
    view.toggleFee(TENUE);

    await view.submit();

    assert.equal(posts.length, 1);
    assert.equal(posts[0].endpoint, '/enrollments');
    assert.equal(posts[0].body.type, 'ReEnrollment');
    assert.deepEqual(posts[0].body.optionalFeeCategoryIds, [UNIFORME]);
});

test('nouvelle inscription : le payload envoyé au serveur porte les frais cochés', async () => {
    const { view, posts } = enrollments();
    withFees(view);
    view.mode = 'NewEnrollment';
    view.activeYear = { id: 'y1', isActive: true };
    view.form.fullName = 'Awa Ndiaye';

    await view.submit();

    assert.equal(posts.length, 1);
    assert.equal(posts[0].body.type, 'NewEnrollment');
    assert.deepEqual(posts[0].body.optionalFeeCategoryIds.slice().sort(), [TENUE, UNIFORME].sort());
});

test('le chargement des références marque chaque frais optionnel d\'après sa catégorie', async () => {
    const apiGet = async (endpoint) => {
        if (endpoint === '/classrooms') return [{ id: CLASS, name: '6e', level: 'Collège' }];
        if (endpoint === '/finance/fee-categories') {
            return [
                { id: INSCRIPTION, name: 'Inscription', isRecurring: false, isOptional: false },
                { id: UNIFORME, name: 'Uniforme', isRecurring: false, isOptional: true }
            ];
        }
        if (endpoint === '/finance/fees') {
            return [
                { classroomId: CLASS, feeCategoryId: INSCRIPTION, feeCategoryName: 'Inscription', amount: 10000 },
                { classroomId: CLASS, feeCategoryId: UNIFORME, feeCategoryName: 'Uniforme', amount: 25000 }
            ];
        }
        if (endpoint === '/school-years') return [{ id: 'y1', isActive: true, label: '2026-2027' }];
        if (endpoint === '/schools/current/settings') return { tuitionMonthsPerYear: 10 };
        return [];
    };
    const { view } = enrollments({ apiGet });

    await view.loadReferenceData();
    view.form.classroomId = CLASS;

    const byId = Object.fromEntries(plain(view.feeChoices()).map((c) => [c.feeCategoryId, c.isOptional]));
    assert.deepEqual(byId, { [INSCRIPTION]: false, [UNIFORME]: true });
});
