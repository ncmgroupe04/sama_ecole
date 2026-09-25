/**
 * Garde-fou « Mode test » (wwwroot/js/school-mode-guard.js + setup-assistant.js + pastille de auth.js).
 *
 * Ce qui compte : la modale d'avertissement s'ouvre UNIQUEMENT en mode test, sur un module de saisie
 * réelle (Inscriptions, Caisse, Notes), une fois par session, et — pour les rôles qui paramètrent —
 * une fois le paramétrage de base terminé ; jamais en mode réel, jamais sur une autre page, jamais
 * sur une lecture de mode qui échoue (on ne crie pas « mode test » sans en être sûr).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function createSessionStorage() {
    const store = new Map();
    return {
        getItem: (k) => (store.has(k) ? store.get(k) : null),
        setItem: (k, v) => store.set(k, String(v)),
        removeItem: (k) => store.delete(k)
    };
}

function boot({
    role = 'Directeur', authed = true, path = '/inscriptions', isLive = false,
    modeFails = false, setupProgress, sessionStorage = createSessionStorage()
} = {}) {
    const calls = [];
    const closed = [];
    const api = {
        get: async (endpoint) => {
            calls.push(endpoint);
            if (modeFails) throw new Error('panne réseau');
            return { isLive };
        }
    };
    const ctx = loadScripts(['school-mode-guard.js'], {
        preload: {
            auth: { isAuthenticated: () => authed, role },
            api,
            sessionStorage,
            location: { pathname: path, assign: (url) => { ctx.assigned = url; } },
            closeAllModals: () => closed.push(true),
            ...(setupProgress !== undefined ? { setupProgress } : {})
        }
    });
    ctx.calls = calls;
    ctx.closed = closed;
    ctx.sessionStorage = sessionStorage;
    return ctx;
}

async function mount(options) {
    const ctx = boot(options);
    const guard = ctx.component('liveModeGuard');
    await flush();
    return { guard, ctx };
}

// ------------------------------------------------------------------ Lecture partagée du mode

test('window.schoolMode.get() ne déclenche qu\'une requête, même appelé plusieurs fois', async () => {
    const ctx = boot();
    await Promise.all([ctx.window.schoolMode.get(), ctx.window.schoolMode.get()]);
    assert.deepEqual(ctx.calls, ['/schools/current/mode']);
});

test('window.schoolMode.get() ne mémorise pas un échec : on retentera', async () => {
    const ctx = boot({ modeFails: true });
    await assert.rejects(ctx.window.schoolMode.get());
    await assert.rejects(ctx.window.schoolMode.get());
    assert.equal(ctx.calls.length, 2);
});

// ------------------------------------------------------------------ Déclenchement de la modale

test('mode test + page de saisie + paramétrage de base terminé : la modale s\'ouvre (Directeur)', async () => {
    const { guard } = await mount({ setupProgress: { baseComplete: true } });
    assert.equal(guard.open, true);
    assert.equal(guard.isDirecteur, true);
});

for (const path of ['/inscriptions', '/caisse', '/notes', '/notes/', '/Caisse']) {
    test(`la page ${path} est gardée`, async () => {
        const { guard } = await mount({ path, setupProgress: { baseComplete: true } });
        assert.equal(guard.open, true);
    });
}

for (const path of ['/eleves', '/parametres', '/classes', '/notes-de-frais', '/']) {
    test(`la page ${path} n'est PAS gardée`, async () => {
        const { guard, ctx } = await mount({ path, setupProgress: { baseComplete: true } });
        assert.equal(guard.open, false);
        assert.equal(ctx.calls.length, 0, 'aucune requête inutile hors des pages gardées');
    });
}

test('mode réel : aucune modale', async () => {
    const { guard } = await mount({ isLive: true, setupProgress: { baseComplete: true } });
    assert.equal(guard.open, false);
});

test('lecture du mode en échec : aucune modale (défaut prudent)', async () => {
    const { guard } = await mount({ modeFails: true, setupProgress: { baseComplete: true } });
    assert.equal(guard.open, false);
});

test('paramétrage de base INCOMPLET : pas de modale pour le Directeur', async () => {
    const { guard } = await mount({ setupProgress: { baseComplete: false } });
    assert.equal(guard.open, false);
});

test('Secrétariat : soumis au même critère de paramétrage de base', async () => {
    const incomplete = await mount({ role: 'Secretariat', setupProgress: { baseComplete: false } });
    assert.equal(incomplete.guard.open, false);
    const complete = await mount({ role: 'Secretariat', setupProgress: { baseComplete: true } });
    assert.equal(complete.guard.open, true);
});

test('Finance / Enseignant : avertis sans critère de paramétrage, et sans bouton vers les Paramètres', async () => {
    for (const role of ['Finance', 'Enseignant']) {
        const { guard } = await mount({ role, path: role === 'Finance' ? '/caisse' : '/notes' });
        assert.equal(guard.open, true, role);
        assert.equal(guard.isDirecteur, false, role);
    }
});

test('Super Admin et session non authentifiée : rien', async () => {
    assert.equal((await mount({ role: 'SuperAdmin' })).guard.open, false);
    assert.equal((await mount({ authed: false })).guard.open, false);
});

test('attend le signal de l\'assistant de démarrage quand il n\'est pas encore connu', async () => {
    const { guard, ctx } = await mount({}); // setupProgress absent
    assert.equal(guard.open, false, 'tant que le paramétrage n\'est pas évalué, on n\'ouvre pas');

    ctx.window.setupProgress = { baseComplete: true };
    ctx.window.emit('setup-progress', { baseComplete: true });
    await flush();
    assert.equal(guard.open, true);
});

test('signal « base incomplète » de l\'assistant : reste fermée', async () => {
    const { guard, ctx } = await mount({});
    ctx.window.emit('setup-progress', { baseComplete: false });
    await flush();
    assert.equal(guard.open, false);
});

// ------------------------------------------------------------------ Une fois par session

test('une fois par session : la seconde arrivée sur une page gardée ne rouvre pas la modale', async () => {
    const sessionStorage = createSessionStorage();
    const first = await mount({ sessionStorage, setupProgress: { baseComplete: true } });
    assert.equal(first.guard.open, true);

    const second = await mount({ sessionStorage, path: '/caisse', setupProgress: { baseComplete: true } });
    assert.equal(second.guard.open, false);
    assert.equal(second.ctx.calls.length, 0, 'pas même de requête : le drapeau est testé en premier');
});

test('sessionStorage indisponible : la modale s\'ouvre quand même, sans exception', async () => {
    const broken = {
        getItem() { throw new Error('bloqué'); },
        setItem() { throw new Error('bloqué'); }
    };
    const { guard } = await mount({ sessionStorage: broken, setupProgress: { baseComplete: true } });
    assert.equal(guard.open, true);
});

// ------------------------------------------------------------------ Interaction avec les autres modales

test('ferme les autres modales avant de s\'ouvrir (l\'assistant de démarrage peut s\'ouvrir seul)', async () => {
    const { ctx } = await mount({ setupProgress: { baseComplete: true } });
    assert.equal(ctx.closed.length, 1);
});

// ------------------------------------------------------------------ Bouton d'action

test('« Aller aux Paramètres Système » mène à l\'onglet système, ancre du bloc de bascule', async () => {
    const { guard, ctx } = await mount({ setupProgress: { baseComplete: true } });
    guard.goToSettings();
    assert.equal(ctx.assigned, '/parametres?tab=securite#mode-reel');
});

test('close() referme la modale', async () => {
    const { guard } = await mount({ setupProgress: { baseComplete: true } });
    guard.close();
    assert.equal(guard.open, false);
});

// ------------------------------------------------------------------ Critère « paramétrage de base » (assistant)

const ASSISTANT_TABLE = (overrides = {}) => ({
    '/school-years': [{ isActive: true }],
    '/classrooms': [{ id: 'c1' }],
    '/subjects': [{ id: 's1' }],
    '/finance/fee-categories': [{ id: 'fc1' }],
    '/finance/fees': [{ id: 'f1' }],
    '/teachers': { totalCount: 3, items: [] },
    '/students': { totalCount: 0, items: [] },
    '/schools/current': {},
    '/schools/current/settings': { typeEtablissement: 'Prive' },
    ...overrides
});

async function mountAssistant(table) {
    const ctx = loadScripts(['setup-assistant.js'], {
        preload: {
            auth: { isAuthenticated: () => true, role: 'Directeur' },
            api: { get: async (endpoint) => table[endpoint.split('?')[0]] }
        }
    });
    const events = [];
    ctx.window.addEventListener('setup-progress', (e) => events.push(e.detail));
    const assistant = ctx.component('setupAssistant');
    await flush();
    return { assistant, ctx, events };
}

test('assistant : base terminée SANS première inscription ni SIMEN (ce que la modale protège)', async () => {
    const { assistant, ctx, events } = await mountAssistant(ASSISTANT_TABLE());
    assert.equal(assistant.baseComplete, true);
    assert.equal(assistant.percent < 100, true, 'le parcours complet reste inachevé');
    assert.deepEqual(plain(events.at(-1)), { baseComplete: true });
    assert.equal(ctx.window.setupProgress.baseComplete, true);
});

test('assistant : une étape de base manquante (enseignants) => base incomplète', async () => {
    const { assistant, events } = await mountAssistant(
        ASSISTANT_TABLE({ '/teachers': { totalCount: 0, items: [] } }));
    assert.equal(assistant.baseComplete, false);
    assert.deepEqual(plain(events.at(-1)), { baseComplete: false });
});

test('assistant : établissement public — frais non applicable, la base peut être complète', async () => {
    const { assistant } = await mountAssistant(ASSISTANT_TABLE({
        '/finance/fees': [],
        '/schools/current/settings': { typeEtablissement: 'Public' }
    }));
    assert.equal(assistant.baseComplete, true);
});

// ------------------------------------------------------------------ Pastille de la barre supérieure

test('pastille : s\'appuie sur la lecture partagée du mode et bascule sur isLive', async () => {
    const calls = [];
    const api = { get: async (e) => { calls.push(e); return { isLive: false }; } };
    const ctx = loadScripts(['auth.js', 'school-mode-guard.js'], { preload: { api } });
    ctx.window.auth.isAuthenticated = () => true;

    const badge = ctx.component('sandboxModeBadge');
    await flush();

    assert.equal(badge.loaded, true);
    assert.equal(badge.isLive, false);
    assert.deepEqual(calls, ['/schools/current/mode']);
});
