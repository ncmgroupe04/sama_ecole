/**
 * Écran Coran/Franco-Arabe (/coran) — spec docs/superpowers/specs/2026-09-29-coran-screen-design.md.
 *
 * Section 1 : store `schoolConfig.coranEnabled` (auth.js), sur le modèle exact d'`internatEnabled`
 * (même précaution "sûr par défaut = caché" : module réservé/inerte tant que non activé).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

/** JWT minimal — seul le payload compte, readClaims() ne vérifie jamais la signature. */
function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

/** Charge auth.js avec une session Directeur posée ; `settings` = réponse de GET /schools/current/settings. */
function bootAuth({ settings, failSettings = false } = {}) {
    const ctx = loadScripts(['auth.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') {
                        if (failSettings) throw new Error('réseau');
                        return settings ? settings() : {};
                    }
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                put: async (_endpoint, body) => body,
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            pdfPreview: { state: () => ({}) },
            location: { href: 'https://localhost/x', search: '' },
            history: { replaceState() {} }
        }
    });
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role: 'Directeur' }), expiresIn: 900 });
    return ctx;
}

test('coranEnabled suit isCoranModuleEnabled du serveur quand vrai', async () => {
    const ctx = bootAuth({ settings: () => ({ isCoranModuleEnabled: true }) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.coranEnabled, true);
});

test('coranEnabled reste false (masqué) tant que la réponse manque ou en cas d\'erreur réseau', async () => {
    const ctx = bootAuth({ failSettings: true });
    const store = ctx.store('schoolConfig');

    assert.equal(store.coranEnabled, false, 'avant la réponse');
    await store.init();
    assert.equal(store.coranEnabled, false, 'après une erreur réseau — sûr par défaut = caché');
});

test('coranEnabled reste false si le serveur omet le champ (ancienne réponse)', async () => {
    const ctx = bootAuth({ settings: () => ({}) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.equal(store.coranEnabled, false);
});

test('sidebarNav.coranEnabled délègue au store partagé', async () => {
    const ctx = bootAuth({ settings: () => ({ isCoranModuleEnabled: true }) });
    await ctx.store('schoolConfig').init();

    const nav = ctx.component('sidebarNav');

    assert.equal(nav.coranEnabled, true);
    assert.deepEqual(plain({ x: nav.coranEnabled }), { x: true });
});

// ---------------------------------------------------------------- 2. coranPage() — classe et onglets

const CLASSROOMS = [
    { id: 'c-cm2', name: 'CM2 A', level: 'Primaire' },
    { id: 'c-6a', name: '6e A', level: 'Collège' }
];

/**
 * Charge coran.js avec une API factice qui enregistre chaque appel. `progress`/`evaluations` sont des
 * fonctions : elles reçoivent l'URL demandée et renvoient les lignes de classe (ou lèvent) — un test
 * peut ainsi faire varier la réponse d'un appel à l'autre.
 */
function boot({ role = 'Directeur', progress, evaluations, failWith = {} } = {}) {
    const calls = [];
    const record = (method) => async (endpoint, body) => {
        calls.push({ method, endpoint, body });
        if (failWith[method]) throw failWith[method];
        return { rowVersion: 1 };
    };

    const ctx = loadScripts(['coran.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/classrooms') return CLASSROOMS;
                    if (endpoint.startsWith('/quran/progress/classroom/')) {
                        if (failWith.progress) throw failWith.progress;
                        return progress ? progress(endpoint) : [];
                    }
                    if (endpoint.startsWith('/quran/evaluations/classroom/')) {
                        if (failWith.evaluations) throw failWith.evaluations;
                        return evaluations ? evaluations(endpoint) : [];
                    }
                    return [];
                },
                post: record('POST'),
                put: record('PUT'),
                toMessage: (e, fallback) => (e && e.message) || fallback
            }
        }
    });
    const view = ctx.component('coranPage');
    return { view, calls };
}

test('au chargement : la liste des classes est récupérée', async () => {
    const { view, calls } = boot();
    await view.init();

    assert.equal(view.loadingClassrooms, false);
    assert.deepEqual(plain(view.classrooms.map(c => c.id)), ['c-cm2', 'c-6a']);
    assert.ok(calls.some(c => c.endpoint === '/classrooms'));
});

test('une erreur réseau au chargement des classes renseigne `error`', async () => {
    const { view } = boot();
    view.classrooms = [];
    // Deuxième instance, dont le GET échoue systématiquement.
    const { view: broken } = (() => {
        const ctx = loadScripts(['coran.js'], {
            preload: {
                auth: { role: 'Directeur' },
                api: {
                    get: async () => { throw new Error('réseau'); },
                    post: async () => ({}), put: async () => ({}),
                    toMessage: (_e, fallback) => fallback
                }
            }
        });
        return { view: ctx.component('coranPage') };
    })();
    await broken.init();

    assert.match(broken.error, /classes/i);
});

test('sélectionner une classe pose selectedClassroomId', async () => {
    const { view } = boot();
    await view.init();

    await view.selectClassroom('c-cm2');

    assert.equal(view.selectedClassroomId, 'c-cm2');
});

test('activeTab démarre sur "progress"', async () => {
    const { view } = boot();
    assert.equal(view.activeTab, 'progress');
});
