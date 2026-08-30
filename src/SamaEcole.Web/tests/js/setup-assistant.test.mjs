/**
 * Assistant de Premier Paramétrage (wwwroot/js/setup-assistant.js).
 *
 * Ce qui compte ici : le pourcentage d'avancement et le VERROU séquentiel se déduisent fidèlement de
 * l'état renvoyé par l'API, une route qui échoue ne coche jamais son étape par défaut, et un rôle qui
 * n'a rien à paramétrer ne déclenche aucun appel.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

const FRESH = () => ({
    '/school-years': [],
    '/classrooms': [],
    '/subjects': [],
    '/finance/fee-categories': [],
    '/finance/fees': [],
    '/teachers': { totalCount: 0, items: [] },
    '/students': { totalCount: 0, items: [] },
    '/schools/current': {},
    '/schools/current/settings': { typeEtablissement: 'Prive' }
});

const FULL = () => ({
    '/school-years': [{ isActive: true }],
    '/classrooms': [{ id: 'c1' }],
    '/subjects': [{ id: 's1' }],
    '/finance/fee-categories': [{ id: 'fc1' }],
    '/finance/fees': [{ id: 'f1' }],
    '/teachers': { totalCount: 3, items: [] },
    '/students': { totalCount: 5, items: [] },
    '/schools/current': { nationalSchoolCode: 'DK-001' },
    '/schools/current/settings': { typeEtablissement: 'Prive' }
});

function bootAssistant({ role = 'Directeur', authed = true, responses = {}, failing = [] } = {}) {
    const calls = [];
    const table = { ...FRESH(), ...responses };
    const api = {
        get: async (endpoint) => {
            calls.push(endpoint);
            const key = endpoint.split('?')[0];
            if (failing.includes(key)) throw new Error('panne ' + key);
            return table[key];
        }
    };
    const ctx = loadScripts(['setup-assistant.js'], {
        preload: { auth: { isAuthenticated: () => authed, role }, api }
    });
    return { ctx, calls };
}

async function mount(options) {
    const { ctx, calls } = bootAssistant(options);
    const a = ctx.component('setupAssistant');
    await flush();
    return { a, ctx, calls };
}

const stateOf = (a, key) => a.steps.find((s) => s.key === key).state;

test('établissement vierge : 0 %, toutes les étapes à faire, seule la 1re déverrouillée', async () => {
    const { a } = await mount();

    assert.equal(a.eligible, true);
    assert.equal(a.loaded, true);
    assert.equal(a.percent, 0);
    assert.ok(a.steps.every((s) => s.state === 'todo'));
    assert.equal(a.isUnlocked(0), true);
    assert.equal(a.isUnlocked(1), false);
    assert.equal(a.isLocked(1), true);
});

test('établissement complet : 100 %, allDone', async () => {
    const { a } = await mount({ responses: FULL() });

    assert.equal(a.percent, 100);
    assert.equal(a.allDone, true);
    assert.ok(a.steps.every((s) => s.state === 'done'));
    assert.equal(a.isUnlocked(5), true);
});

test('avancement partiel : année + pédagogie faites → 33 %, frais déverrouillé, enseignants verrouillé', async () => {
    const { a } = await mount({
        responses: {
            '/school-years': [{ isActive: true }],
            '/classrooms': [{ id: 'c1' }],
            '/subjects': [{ id: 's1' }]
        }
    });

    assert.equal(stateOf(a, 'annee'), 'done');
    assert.equal(stateOf(a, 'pedagogie'), 'done');
    assert.equal(a.percent, 33); // round(2 / 6 * 100)
    assert.equal(a.isUnlocked(2), true);  // frais
    assert.equal(a.isLocked(3), true);    // enseignants
    assert.equal(a.firstActionableIndex, 2);
});

test('établissement public : l’étape « grille tarifaire » devient non applicable et sort du calcul', async () => {
    const { a } = await mount({
        responses: {
            ...FULL(),
            '/schools/current/settings': { typeEtablissement: 'Public' },
            // même vide, l'étape ne doit pas compter
            '/finance/fee-categories': [],
            '/finance/fees': []
        }
    });

    assert.equal(stateOf(a, 'frais'), 'na');
    assert.equal(a.applicableSteps.length, 5);
    assert.equal(a.percent, 100); // 5 étapes applicables, toutes faites
    // le verrou saute par-dessus une étape non applicable
    assert.equal(a.isUnlocked(3), true);
});

test('une route en panne laisse SON étape à faire, sans fausser les autres', async () => {
    const { a } = await mount({ responses: FULL(), failing: ['/school-years'] });

    assert.equal(stateOf(a, 'annee'), 'todo');
    assert.equal(stateOf(a, 'pedagogie'), 'done');
    assert.equal(a.percent, 83); // round(5 / 6 * 100)
    assert.equal(a.isUnlocked(1), false); // l'année n'étant pas active, la suite n'est plus « déverrouillée »
    assert.equal(a.firstActionableIndex, 0); // on repointe vers l'étape en échec
});

test('rôle non concerné (Finance) : assistant masqué, aucun appel API', async () => {
    const { a, calls } = await mount({ role: 'Finance' });

    assert.equal(a.eligible, false);
    assert.equal(a.loaded, false);
    assert.equal(calls.length, 0);
});

test('session non authentifiée : assistant masqué', async () => {
    const { a, calls } = await mount({ authed: false });

    assert.equal(a.eligible, false);
    assert.equal(calls.length, 0);
});

test('ouverture automatique une seule fois par navigateur', async () => {
    const { ctx } = bootAssistant();

    const first = ctx.component('setupAssistant');
    await flush();
    assert.equal(first.open, true);
    assert.equal(ctx.localStorage.getItem('unikol.setupAssistant.introShown'), '1');

    const second = ctx.component('setupAssistant');
    await flush();
    assert.equal(second.open, false); // le drapeau localStorage empêche la réouverture
});

test('revérifier reflète un paramétrage complété entre-temps', async () => {
    const calls = [];
    let table = FRESH();
    const api = {
        get: async (endpoint) => {
            calls.push(endpoint);
            return table[endpoint.split('?')[0]];
        }
    };
    const ctx = loadScripts(['setup-assistant.js'], {
        preload: { auth: { isAuthenticated: () => true, role: 'Directeur' }, api }
    });

    const a = ctx.component('setupAssistant');
    await flush();
    assert.equal(a.percent, 0);

    table = FULL();
    await a.refresh();
    assert.equal(a.percent, 100);
    assert.equal(a.allDone, true);
});
