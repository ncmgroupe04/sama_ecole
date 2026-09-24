/**
 * Jours ouvrés de l'établissement (Évolution N°3) — côté navigateur.
 *
 *   1. Store `schoolConfig` (auth.js) : noms de jours de l'API → index DayOfWeek, ordre conservé,
 *      `isWorkingDay(date)`.
 *   2. Écran Paramètres (settings.js) : chargement, préréglages, bascule d'un jour, envoi au PUT.
 *   3. Emploi du temps (teachers.js) : la grille suit la semaine de l'école.
 *   4. Écran d'appel (attendance.js) : un jour de repos n'envoie aucune requête.
 *
 * Tout ceci est du CONFORT d'affichage : le vrai verrou est WorkingDayGuard côté serveur (422).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

/** JWT minimal — seul le payload compte, readClaims() ne vérifie jamais la signature. */
function fakeJwt(claims) {
    const b64url = Buffer.from(JSON.stringify(claims), 'utf8')
        .toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    return `header.${b64url}.signature`;
}

const SAT_TO_WED = ['Saturday', 'Sunday', 'Monday', 'Tuesday', 'Wednesday']; // repos jeudi/vendredi

/** Charge auth.js (session Directeur posée) + les scripts demandés ; `settings` = réponse de GET settings. */
function boot(extraScripts, { settings, failSettings = false, extraApi = {}, extraPreload = {} } = {}) {
    const puts = [];
    const ctx = loadScripts(['auth.js', ...extraScripts], {
        preload: {
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') {
                        if (failSettings) throw new Error('réseau');
                        return settings();
                    }
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                put: async (_endpoint, body) => { puts.push(plain(body)); return body; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback }),
                ...extraApi
            },
            pdfPreview: { state: () => ({}) },
            location: { href: 'https://localhost/x', search: '' },
            history: { replaceState() {} },
            ...extraPreload
        }
    });
    ctx.window.auth.saveSession({ accessToken: fakeJwt({ role: 'Directeur' }), expiresIn: 900 });
    return { ctx, puts };
}

const SERVER_SETTINGS = (overrides = {}) => () => ({
    gradingScale: '20',
    studentMatriculeFormat: 'ELEV-{YEAR}-{SEQ:4}',
    teacherMatriculeFormat: 'ENS-{YEAR}-{SEQ:3}',
    autoLogoutMinutes: 10,
    dateFormat: 'dd/MM/yyyy',
    tuitionMonthsPerYear: 9,
    typeEtablissement: 'Prive',
    isPedagogyEnabled: true,
    isFinanceEnabled: true,
    isInternatEnabled: false,
    isCoranModuleEnabled: false,
    gradeEditWindowDays: 7,
    evaluationPeriodType: 'Trimester',
    customPeriodCount: 3,
    workingDays: SAT_TO_WED,
    ...overrides
});

// ---------------------------------------------------------------- 1. Store schoolConfig

test('le store convertit les jours de l\'API en index DayOfWeek, dans l\'ordre d\'affichage du serveur', async () => {
    const { ctx } = boot([], { settings: SERVER_SETTINGS() });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.deepEqual(plain(store.workingDays), [6, 0, 1, 2, 3]);
    assert.equal(store.isWorkingDay('2026-09-24'), false, 'jeudi : repos');
    assert.equal(store.isWorkingDay('2026-09-26'), true, 'samedi : ouvré');
    assert.equal(store.isWorkingDay('2026-09-27'), true, 'dimanche : ouvré ici');
});

test('le store retombe sur lundi → samedi tant que la réponse manque ou en cas d\'erreur', async () => {
    const { ctx } = boot([], { settings: SERVER_SETTINGS(), failSettings: true });
    const store = ctx.store('schoolConfig');

    assert.deepEqual(plain(store.workingDays), [1, 2, 3, 4, 5, 6], 'avant la réponse');
    await store.init();
    assert.deepEqual(plain(store.workingDays), [1, 2, 3, 4, 5, 6], 'après une erreur réseau');
    assert.equal(store.isWorkingDay('2026-09-27'), false, 'dimanche : repos par défaut');
});

test('une réponse sans workingDays (ancien serveur) garde la semaine par défaut', async () => {
    const { ctx } = boot([], { settings: SERVER_SETTINGS({ workingDays: undefined }) });
    const store = ctx.store('schoolConfig');

    await store.init();

    assert.deepEqual(plain(store.workingDays), [1, 2, 3, 4, 5, 6]);
});

test('isWorkingDay ne bloque pas une date illisible : le serveur juge', async () => {
    const { ctx } = boot([], { settings: SERVER_SETTINGS() });

    assert.equal(ctx.store('schoolConfig').isWorkingDay(''), true);
    assert.equal(ctx.store('schoolConfig').isWorkingDay('pas-une-date'), true);
});

// ---------------------------------------------------------------- 2. Écran Paramètres

async function settingsView(settings = SERVER_SETTINGS()) {
    const { ctx, puts } = boot(['settings.js'], { settings });
    const view = ctx.component('settingsView');
    await flush();
    return { view, puts };
}

test('Paramètres charge les jours ouvrés du serveur', async () => {
    const { view } = await settingsView();

    assert.deepEqual(plain(view.config.workingDays), SAT_TO_WED);
});

test('le préréglage « sat-wed » donne repos jeudi/vendredi, « mon-fri » lundi → vendredi', async () => {
    const { view } = await settingsView(SERVER_SETTINGS({ workingDays: ['Monday'] }));

    view.applyWeekPreset('sat-wed');
    assert.deepEqual(plain(view.config.workingDays), SAT_TO_WED);

    view.applyWeekPreset('mon-fri');
    assert.deepEqual(plain(view.config.workingDays), ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday']);
});

test('toggleWorkingDay retire puis remet un jour', async () => {
    const { view } = await settingsView();

    view.toggleWorkingDay('Saturday');
    assert.equal(view.config.workingDays.includes('Saturday'), false);

    view.toggleWorkingDay('Saturday');
    assert.equal(view.config.workingDays.includes('Saturday'), true);
});

test('retirer le dernier jour ouvré est refusé côté écran', async () => {
    const { view } = await settingsView(SERVER_SETTINGS({ workingDays: ['Monday'] }));

    view.toggleWorkingDay('Monday');

    assert.deepEqual(plain(view.config.workingDays), ['Monday']);
});

test('saveConfig envoie les jours ouvrés modifiés', async () => {
    const { view, puts } = await settingsView();

    view.applyWeekPreset('mon-sat');
    await view.saveConfig();

    assert.deepEqual(puts[0].workingDays, ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']);
});

test('saveConfig conserve les jours ouvrés du serveur quand l\'écran n\'y a pas touché', async () => {
    const { view, puts } = await settingsView();

    view.config.autoLogoutMinutes = 20;
    await view.saveConfig();

    assert.equal(puts[0].autoLogoutMinutes, 20);
    assert.deepEqual(puts[0].workingDays, SAT_TO_WED, 'un autre réglage ne réinitialise pas la semaine');
});

// ---------------------------------------------------------------- 3. Emploi du temps

async function teachersView(slots = []) {
    const { ctx } = boot(['teachers.js'], { settings: SERVER_SETTINGS() });
    await ctx.store('schoolConfig').init();
    const view = ctx.component('teachersView');
    view.scheduleSlots = slots;
    return view;
}

test('la grille affiche les jours ouvrés dans l\'ordre de la semaine de l\'école', async () => {
    const view = await teachersView();

    assert.deepEqual(
        plain(view.daysOfWeek),
        [
            { value: 6, label: 'Samedi', isRest: false },
            { value: 0, label: 'Dimanche', isRest: false },
            { value: 1, label: 'Lundi', isRest: false },
            { value: 2, label: 'Mardi', isRest: false },
            { value: 3, label: 'Mercredi', isRest: false }
        ]);
});

test('un créneau hérité d\'un jour de repos garde une colonne « repos » en fin de grille', async () => {
    const view = await teachersView([{ id: 'x', dayOfWeek: 4, startTime: '08:00:00' }]);

    assert.equal(view.daysOfWeek.length, 6);
    assert.deepEqual(plain(view.daysOfWeek[5]), { value: 4, label: 'Jeudi', isRest: true });
});

test('le sélecteur « Jour » du formulaire ne propose que les jours ouvrés', async () => {
    const view = await teachersView([{ id: 'x', dayOfWeek: 4, startTime: '08:00:00' }]);

    assert.deepEqual(plain(view.workingDayOptions).map((o) => o.value), [6, 0, 1, 2, 3]);
});

// ---------------------------------------------------------------- 4. Écran d'appel

async function attendanceView() {
    const requests = [];
    const { ctx } = boot(['attendance.js'], {
        settings: SERVER_SETTINGS(),
        extraApi: {
            get: async (endpoint) => {
                requests.push(endpoint);
                if (endpoint === '/schools/current/settings') return SERVER_SETTINGS()();
                if (endpoint.startsWith('/attendance/roster')) return { students: [], alreadySubmitted: false };
                return [];
            }
        }
    });
    await ctx.store('schoolConfig').init();
    const view = ctx.component('attendanceView');
    await flush();
    Object.assign(view.filters, { classroomId: 'c1', subjectId: 's1', period: 'Matin' });
    return { view, requests };
}

test('un jour de repos est signalé et n\'envoie aucune requête de feuille d\'appel', async () => {
    const { view, requests } = await attendanceView();

    view.filters.date = '2026-09-24'; // jeudi : repos
    assert.equal(view.isRestDay, true);
    assert.equal(Boolean(view.canLoad), false);

    await view.loadRoster();

    assert.equal(requests.some((r) => r.startsWith('/attendance/roster')), false, 'aucune requête partie');
});

test('un jour ouvré charge la feuille d\'appel normalement', async () => {
    const { view, requests } = await attendanceView();

    view.filters.date = '2026-09-26'; // samedi : ouvré
    assert.equal(view.isRestDay, false);
    assert.equal(Boolean(view.canLoad), true);

    await view.loadRoster();

    assert.equal(requests.some((r) => r.startsWith('/attendance/roster')), true);
});
