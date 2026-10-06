/**
 * Cycles gérés par l'établissement (SchoolSettings.ManagedCycles) côté navigateur :
 *   1. règles pures (managed-cycles.js) ;
 *   2. store schoolConfig (auth.js) — lecture des réglages et valeur de repli ;
 *   3. les trois écrans qui s'y adaptent : classes (classrooms.js), inscription (enrollments.js), examens (exams.js).
 *
 * CONFORT d'affichage : un niveau, une classe ou un type déjà en cours d'usage ne disparaît jamais d'un sélecteur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const cycles = () => loadScripts([]).window.managedCycles;

const ALL = ['Maternelle', 'Primaire', 'College', 'Lycee'];

// ---------------------------------------------------------------- 1. règles pures

test('normalize : toute valeur absente, vide ou sans cycle connu retombe sur TOUS les cycles', () => {
    const m = cycles();

    for (const bad of [undefined, null, 'Primaire', {}, [], ['Creche'], ['x', 'y'], [1, 2]]) {
        assert.deepEqual(plain(m.normalize(bad)), ALL, JSON.stringify(bad));
    }
});

test('normalize : garde l\'ordre canonique, ignore les noms inconnus et les doublons', () => {
    const m = cycles();

    assert.deepEqual(plain(m.normalize(['Lycee', 'Primaire'])), ['Primaire', 'Lycee']);
    assert.deepEqual(plain(m.normalize(['Primaire', 'Inconnu', 'Primaire'])), ['Primaire']);
});

test('cycleOfLevel : Crèche et Maternelle relèvent du même cycle ; casse et accents ignorés', () => {
    const m = cycles();

    assert.equal(m.cycleOfLevel('Crèche'), 'Maternelle');
    assert.equal(m.cycleOfLevel('Maternelle'), 'Maternelle');
    assert.equal(m.cycleOfLevel('Primaire'), 'Primaire');
    assert.equal(m.cycleOfLevel('Collège'), 'College');
    assert.equal(m.cycleOfLevel('  COLLEGE '), 'College');
    assert.equal(m.cycleOfLevel('Lycée'), 'Lycee');
});

test('cycleOfLevel : un niveau inconnu n\'a pas de cycle', () => {
    const m = cycles();

    assert.equal(m.cycleOfLevel('Daara'), null);
    assert.equal(m.cycleOfLevel(''), null);
    assert.equal(m.cycleOfLevel(undefined), null);
});

test('isManaged : un cycle inconnu ou absent n\'est jamais caché', () => {
    const m = cycles();

    assert.equal(m.isManaged(['Primaire'], 'Primaire'), true);
    assert.equal(m.isManaged(['Primaire'], 'Lycee'), false);
    assert.equal(m.isManaged(['Primaire'], null), true);
    assert.equal(m.isManaged(['Primaire'], 'Inconnu'), true);
});

test('examTypes : CFEE / BFEM / BAC suivent Primaire / Collège / Lycée', () => {
    const m = cycles();

    assert.deepEqual(plain(m.examTypes(['Maternelle', 'Primaire'])), ['CFEE']);
    assert.deepEqual(plain(m.examTypes(['Primaire', 'College'])), ['CFEE', 'BFEM']);
    assert.deepEqual(plain(m.examTypes(['College', 'Lycee'])), ['BFEM', 'BAC']);
    assert.deepEqual(plain(m.examTypes(ALL)), ['CFEE', 'BFEM', 'BAC']);
});

test('examTypes : sans aucun cycle d\'examen (Maternelle seule), les trois restent proposés — jamais un sélecteur vide', () => {
    assert.deepEqual(plain(cycles().examTypes(['Maternelle'])), ['CFEE', 'BFEM', 'BAC']);
});

// ---------------------------------------------------------------- 2. store schoolConfig

function store({ settings, fail = false } = {}) {
    const ctx = loadScripts(['auth.js'], {
        preload: {
            api: { get: async () => { if (fail) throw new Error('réseau'); return settings; } }
        }
    });
    const b64 = Buffer.from(JSON.stringify({ sub: 'u', schoolId: 's', role: 'Directeur', exp: Math.floor(Date.now() / 1000) + 900 }))
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    ctx.window.auth.saveSession({ accessToken: `h.${b64}.s`, expiresIn: 900 }, 'd@test.sn');
    return ctx.store('schoolConfig');
}

const SETTINGS = (managedCycles) => ({ isPedagogyEnabled: true, isFinanceEnabled: true, workingDays: ['Monday'], profileEtablissement: 'General', managedCycles });

test('le store démarre sur tous les cycles (on n\'affiche jamais moins que ce que l\'école avait)', () => {
    const s = store();

    assert.deepEqual(plain(s.managedCycles), ALL);
    assert.equal(s.isCycleManaged('Lycee'), true);
});

test('le store lit les cycles gérés des réglages', async () => {
    const s = store({ settings: SETTINGS(['Maternelle', 'Primaire']) });

    await s.init();

    assert.deepEqual(plain(s.managedCycles), ['Maternelle', 'Primaire']);
    assert.equal(s.isCycleManaged('Primaire'), true);
    assert.equal(s.isCycleManaged('College'), false);
    assert.equal(s.isCycleManaged('Lycee'), false);
});

test('le store retombe sur tous les cycles si les réglages n\'en portent pas (API ancienne) ou en cas d\'erreur', async () => {
    const withoutField = store({ settings: SETTINGS(undefined) });
    await withoutField.init();
    assert.deepEqual(plain(withoutField.managedCycles), ALL);

    const failing = store({ fail: true });
    await failing.init();
    assert.deepEqual(plain(failing.managedCycles), ALL);
});

// ---------------------------------------------------------------- 3a. écran des classes

async function classrooms(managedCycles) {
    const ctx = loadScripts(['auth.js', 'classrooms.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            api: {
                get: async () => [],
                toMessage: (_e, f) => f,
                toFieldErrors: (_e, f) => ({ global: f })
            },
            location: { href: 'https://localhost/classes', search: '' }
        }
    });
    ctx.store('schoolConfig').managedCycles = managedCycles;
    const view = ctx.component('classroomsView');
    await flush();
    return view;
}

test('classes : une école Maternelle + Primaire ne se voit proposer ni Collège ni Lycée', async () => {
    const view = await classrooms(['Maternelle', 'Primaire']);

    assert.deepEqual(plain(view.visibleLevelOptions().map((o) => o.value)), ['Crèche', 'Maternelle', 'Primaire']);
});

test('classes : un Daara « pur » peut ne garder que le Primaire — Crèche/Maternelle disparaissent avec leur cycle', async () => {
    const view = await classrooms(['Primaire']);

    assert.deepEqual(plain(view.visibleLevelOptions().map((o) => o.value)), ['Primaire']);
});

test('classes : un Daara mixte (tous les cycles) garde la liste complète', async () => {
    const view = await classrooms(ALL);

    assert.deepEqual(plain(view.visibleLevelOptions().map((o) => o.value)), ['Crèche', 'Maternelle', 'Primaire', 'Collège', 'Lycée']);
});

test('classes : le niveau DÉJÀ enregistré reste proposé à l\'édition même si son cycle a été retiré', async () => {
    const view = await classrooms(['Maternelle', 'Primaire']);

    const values = plain(view.visibleLevelOptions('Lycée').map((o) => o.value));

    assert.ok(values.includes('Lycée'), 'la donnée existante ne disparaît jamais du sélecteur');
    assert.equal(values.includes('Collège'), false, 'seul le niveau en cours d\'édition est conservé');
});

// ---------------------------------------------------------------- 3b. inscription

function enrollments(settings) {
    const ctx = loadScripts(['enrollments.js'], {
        preload: {
            auth: { role: 'Secretariat' },
            pdfPreview: { state: () => ({}) },
            api: { get: async () => [], toMessage: (_e, f) => f, toFieldErrors: (_e, f) => ({ global: f }) },
            location: { href: 'https://localhost/inscriptions', search: '' }
        }
    });
    const view = ctx.initAlpine().get('enrollmentsView')();
    if (settings !== undefined) view.managedCycles = settings;
    return view;
}

const CLASSES = [
    { id: 'm', name: 'PS A', level: 'Maternelle', cycle: 'Maternelle' },
    { id: 'p', name: 'CM2 A', level: 'Primaire', cycle: 'Primaire' },
    { id: 'c', name: '6e A', level: 'Collège', cycle: 'College' },
    { id: 'l', name: 'Tle S1', level: 'Lycée', cycle: 'Lycee' }
];

test('inscription : seules les classes des cycles gérés sont proposées', () => {
    const view = enrollments(['Primaire', 'College']);
    view.classrooms = CLASSES;

    assert.deepEqual(plain(view.visibleClassrooms().map((c) => c.id)), ['p', 'c']);
});

test('inscription : sans réglage chargé, toutes les classes restent proposées', () => {
    const view = enrollments();
    view.classrooms = CLASSES;

    assert.equal(view.visibleClassrooms().length, 4);
});

test('inscription : la classe déjà choisie dans le formulaire reste proposée même hors cycles gérés', () => {
    const view = enrollments(['Maternelle', 'Primaire']);
    view.classrooms = CLASSES;
    view.form.classroomId = 'l';

    assert.deepEqual(plain(view.visibleClassrooms().map((c) => c.id)), ['m', 'p', 'l']);
});

test('inscription : une classe sans cycle connu n\'est jamais cachée', () => {
    const view = enrollments(['Primaire']);
    view.classrooms = [{ id: 'x', name: 'Classe libre', level: 'Autre' }];

    assert.equal(view.visibleClassrooms().length, 1);
});

// ---------------------------------------------------------------- 3c. examens

function exams(managedCycles) {
    const ctx = loadScripts(['exams.js'], {
        preload: {
            auth: { role: 'Directeur' },
            pdfPreview: { state: () => ({}) },
            api: { get: async () => [], toMessage: (_e, f) => f, toFieldErrors: (_e, f) => ({ global: f }) },
            location: { href: 'https://localhost/examens', search: '' }
        }
    });
    const view = ctx.initAlpine().get('examsView')();
    if (managedCycles) view.managedCycles = managedCycles;
    return view;
}

test('examens : une école Maternelle + Primaire ne propose que le CFEE', () => {
    const view = exams(['Maternelle', 'Primaire']);

    assert.deepEqual(plain(view.examTypeOptions.map((o) => o.value)), ['CFEE']);
});

test('examens : le Collège ajoute le BFEM, le Lycée le BAC', () => {
    assert.deepEqual(plain(exams(['Primaire', 'College']).examTypeOptions.map((o) => o.value)), ['CFEE', 'BFEM']);
    assert.deepEqual(plain(exams(['College', 'Lycee']).examTypeOptions.map((o) => o.value)), ['BFEM', 'BAC']);
});

test('examens : un Daara sans Collège ni Lycée ne se voit pas proposer BFEM/BAC', () => {
    assert.deepEqual(plain(exams(['Primaire']).examTypeOptions.map((o) => o.value)), ['CFEE']);
});

test('examens : sans réglage chargé, les trois types restent proposés', () => {
    assert.deepEqual(plain(exams().examTypeOptions.map((o) => o.value)), ['CFEE', 'BFEM', 'BAC']);
});

test('examens : le type par défaut d\'une nouvelle session est un type proposé (BFEM si possible, sinon le premier)', () => {
    const elementary = exams(['Maternelle', 'Primaire']);
    elementary.schoolYears = [{ id: 'y1', label: '2026', isActive: true }];
    elementary.openCreateSession();
    assert.equal(elementary.newSession.examType, 'CFEE', 'jamais BFEM, qui ne serait plus une option du sélecteur');

    const general = exams(ALL);
    general.schoolYears = [{ id: 'y1', label: '2026', isActive: true }];
    general.openCreateSession();
    assert.equal(general.newSession.examType, 'BFEM');

    const lyceeOnly = exams(['College', 'Lycee']);
    lyceeOnly.schoolYears = [{ id: 'y1', label: '2026', isActive: true }];
    lyceeOnly.openCreateSession();
    assert.equal(lyceeOnly.newSession.examType, 'BFEM');
});
