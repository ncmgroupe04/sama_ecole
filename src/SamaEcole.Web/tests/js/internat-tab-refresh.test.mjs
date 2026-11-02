/**
 * Rechargement d'un onglet de /internat à son ouverture (trouvé à la recette navigateur).
 *
 * Les cinq composants chargeaient leurs données UNE fois, à l'ouverture de la page. Après avoir déclaré une sortie dans « Sorties », l'onglet « Pointage »
 * affichait donc encore la feuille d'avant : le pensionnaire en permission n'y était pas verrouillé « absent » (le serveur aurait refusé « présent », mais
 * l'écran était périmé). Désormais, afficher un onglet le recharge — sauf s'il vient d'être chargé, ou si le recharger détruirait une saisie en cours
 * (modale ouverte, enregistrement en cours, pointage non enregistré).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

// ---------------------------------------------------------------- décision pure

function helpers() {
    return loadScripts(['internat-helpers.js']).window.internatHelpers;
}

test('shouldRefreshOnShow : seulement pour SON onglet, quand il est libre et que ses données ne sont pas toutes fraîches', () => {
    const h = helpers();
    const base = { tabKey: 'pointage', shownKey: 'pointage', busy: false, loadedAt: 1000, now: 1000 + h.REFRESH_MIN_GAP_MS };

    assert.equal(h.shouldRefreshOnShow(base), true);
    assert.equal(h.shouldRefreshOnShow({ ...base, shownKey: 'sorties' }), false, 'un autre onglet est affiché');
    assert.equal(h.shouldRefreshOnShow({ ...base, busy: true }), false, 'une saisie est en cours : on ne la détruit pas');
    assert.equal(h.shouldRefreshOnShow({ ...base, now: 1000 + h.REFRESH_MIN_GAP_MS - 1 }), false, 'données chargées à l\'instant');
    assert.equal(h.shouldRefreshOnShow({ ...base, loadedAt: 0 }), true, 'jamais chargé');
});

test('announceTabShown : annonce l\'onglet affiché à tous les composants (événement sur window)', () => {
    const ctx = loadScripts(['internat-helpers.js']);
    const seen = [];
    ctx.window.addEventListener(ctx.window.internatHelpers.TAB_SHOWN_EVENT, (event) => seen.push(event.detail.key));

    ctx.window.internatHelpers.announceTabShown('sorties');

    assert.deepEqual(seen, ['sorties']);
});

// ---------------------------------------------------------------- conteneur à onglets

function bootTabs({ hash = '' } = {}) {
    const replaced = [];
    const ctx = loadScripts(['internat-helpers.js', 'internat.js'], {
        preload: { location: { protocol: 'https:', hostname: 'localhost', pathname: '/internat', hash }, history: { replaceState: (...a) => replaced.push(a[2]) } }
    });
    const seen = [];
    ctx.window.addEventListener(ctx.window.internatHelpers.TAB_SHOWN_EVENT, (event) => seen.push(event.detail.key));
    return { ctx, tabs: ctx.component('internatTabs'), seen, replaced };
}

test('choisir un onglet l\'annonce et met à jour le fragment sans empiler l\'historique', () => {
    const { tabs, seen, replaced } = bootTabs();
    tabs.select('pointage');

    assert.equal(tabs.tab, 'pointage');
    assert.deepEqual(seen, ['pointage']);
    assert.deepEqual(replaced, ['#pointage']);
});

test('modifier le fragment d\'URL sur la page déjà ouverte change d\'onglet et l\'annonce', () => {
    const { ctx, tabs, seen } = bootTabs();
    ctx.window.location.hash = '#sorties';
    ctx.window.dispatchEvent({ type: 'hashchange' });

    assert.equal(tabs.tab, 'sorties');
    assert.deepEqual(seen, ['sorties']);
});

test('un fragment inconnu ou identique à l\'onglet courant n\'annonce rien', () => {
    const { ctx, tabs, seen } = bootTabs({ hash: '#pavillons' });
    assert.equal(tabs.tab, 'pavillons');

    ctx.window.location.hash = '#pavillons';
    ctx.window.dispatchEvent({ type: 'hashchange' });
    assert.deepEqual(seen, [], 'même onglet : rien');
});

// ---------------------------------------------------------------- composants

const AUTH = (role) => ({ role });
const PDF = { state: () => ({ showPdfModal: false, openPdfPreview: async () => {} }) };

function counter() {
    const calls = [];
    // « prefix » terminé par ? ou / : préfixe ; sinon : appel exact (la liste des pavillons n'est pas leurs détails).
    return { calls, count: (prefix) => calls.filter((c) => (/[?/]$/.test(prefix) ? c.startsWith(prefix) : c === prefix)).length };
}

function api(calls, routes) {
    return {
        get: async (endpoint) => {
            calls.push(endpoint);
            for (const [prefix, value] of Object.entries(routes)) if (endpoint.startsWith(prefix)) return typeof value === 'function' ? value(endpoint) : value;
            return null;
        },
        post: async () => ({}), put: async () => ({}), delete: async () => null,
        toMessage: (err, fallback) => (err && err.message) || fallback
    };
}

const PAGE = { items: [], totalCount: 0, page: 1, pageSize: 20 };
const SHEET = (lines = []) => ({ date: '2026-10-11', dormitoryId: 'd1', dormitoryName: 'Pavillon 1', lines });

const COMPONENTS = {
    occupation: {
        scripts: ['internat-helpers.js', 'internat-occupation.js'], name: 'occupationTab', key: 'occupation', list: '/boarding/dormitories',
        routes: { '/boarding/dormitories/': { id: 'd1', name: 'P', rooms: [] }, '/boarding/dormitories': [{ id: 'd1', name: 'P' }], '/boarding/boarders': { totalCount: 0, items: [] } },
        busyFlags: [(t) => { t.assignModal.open = true; }, (t) => { t.loading = true; }]
    },
    pavillons: {
        scripts: ['internat-helpers.js', 'internat-dormitories.js'], name: 'dormitoriesTab', key: 'pavillons', list: '/boarding/dormitories',
        routes: { '/boarding/dormitories': [] },
        busyFlags: [(t) => { t.form.open = true; }, (t) => { t.roomForm.saving = true; }, (t) => { t.removal.open = true; }]
    },
    pensionnaires: {
        scripts: ['internat-helpers.js', 'internat-boarders.js'], name: 'boardersTab', key: 'pensionnaires', list: '/boarding/boarders?', pdf: true,
        routes: { '/boarding/boarders?': PAGE, '/boarding/dormitories': [] },
        busyFlags: [(t) => { t.panel.open = true; }, (t) => { t.panel.saving = true; }, (t) => { t.showPdfModal = true; }]
    },
    sorties: {
        scripts: ['internat-helpers.js', 'internat-leaves.js'], name: 'leavesTab', key: 'sorties', list: '/boarding/leaves?',
        routes: { '/boarding/leaves?': PAGE, '/boarding/dormitories': [] },
        busyFlags: [(t) => { t.declare.open = true; }, (t) => { t.returnModal.open = true; }]
    }
};

async function boot(spec, role = 'Directeur') {
    const { calls, count } = counter();
    const ctx = loadScripts(spec.scripts, { preload: { auth: AUTH(role), softDeleteTrash: { notifyChanged() {}, openFor() {} }, ...(spec.pdf ? { pdfPreview: PDF } : {}), api: api(calls, spec.routes) } });
    const tab = ctx.component(spec.name);
    await flush();
    return { tab, calls, count };
}

for (const [label, spec] of Object.entries(COMPONENTS)) {
    test(`${label} : l'ouverture de l'onglet recharge ses données quand elles ne sont plus fraîches`, async () => {
        const { tab, count } = await boot(spec);
        const before = count(spec.list);
        tab.loadedAt = 0;

        await tab.onTabShown(spec.key);

        assert.equal(count(spec.list), before + 1, 'une nouvelle lecture de la liste');
    });

    test(`${label} : un autre onglet affiché ne déclenche rien`, async () => {
        const { tab, calls } = await boot(spec);
        const before = calls.length;
        tab.loadedAt = 0;

        await tab.onTabShown('un-autre-onglet');

        assert.equal(calls.length, before);
    });

    test(`${label} : des données chargées à l'instant ne sont pas relues (pas de double chargement à l'ouverture de la page)`, async () => {
        const { tab, calls } = await boot(spec);
        const before = calls.length;

        await tab.onTabShown(spec.key);

        assert.equal(calls.length, before, 'chargé il y a moins de 2 s');
    });

    spec.busyFlags.forEach((setBusy, index) => {
        test(`${label} : jamais de rechargement pendant une saisie ou un enregistrement (cas ${index + 1})`, async () => {
            const { tab, calls } = await boot(spec);
            tab.loadedAt = 0;
            setBusy(tab);
            const before = calls.length;

            await tab.onTabShown(spec.key);

            assert.equal(calls.length, before, 'l\'état en cours serait détruit');
        });
    });
}

// ---------------------------------------------------------------- Pointage : le cas qui a motivé la correction

async function bootAttendance(lines = [{ boarderId: 'a', studentName: 'Awa', matricule: 'M-1', roomName: 'C1', bedNumber: 1, onLeave: false, recorded: false, isPresent: null, note: null }]) {
    const { calls, count } = counter();
    let current = SHEET(lines);
    const ctx = loadScripts(['internat-helpers.js', 'attendance-logic.js', 'internat-attendance.js'], {
        preload: {
            auth: AUTH('Directeur'), pdfPreview: PDF,
            api: api(calls, { '/boarding/dormitories': [{ id: 'd1', name: 'Pavillon 1' }], '/boarding/attendance?': () => current })
        }
    });
    ctx.window.internatHelpers.today = () => '2026-10-11';
    const tab = ctx.component('attendanceTab');
    await flush();
    return { tab, calls, count, setSheet: (s) => { current = s; } };
}

test('pointage : après une sortie déclarée ailleurs, rouvrir l\'onglet recharge la feuille et verrouille le pensionnaire en permission', async () => {
    const { tab, setSheet, count } = await bootAttendance();
    assert.equal(tab.lines[0].onLeave, false, 'feuille d\'avant la sortie');
    assert.equal(count('/boarding/attendance?'), 1);

    // La sortie vient d'être déclarée dans l'onglet « Sorties » : le serveur renvoie désormais l'élève en permission, pré-rempli « absent ».
    setSheet(SHEET([{ boarderId: 'a', studentName: 'Awa', matricule: 'M-1', roomName: 'C1', bedNumber: 1, onLeave: true, recorded: false, isPresent: false, note: 'En permission' }]));
    tab.loadedAt = 0;
    await tab.onTabShown('pointage');

    assert.equal(count('/boarding/attendance?'), 2);
    assert.equal(tab.lines[0].onLeave, true);
    tab.setPresence(tab.lines[0], true);
    assert.equal(tab.lines[0].isPresent, false, "le verrou « absent » s'applique");
});

test('pointage : une saisie non enregistrée n\'est JAMAIS jetée par le rechargement', async () => {
    const { tab, count } = await bootAttendance();
    tab.setPresence(tab.lines[0], true);
    tab.loadedAt = 0;
    const before = count('/boarding/attendance?');

    await tab.onTabShown('pointage');

    assert.equal(count('/boarding/attendance?'), before);
    assert.equal(tab.lines[0].isPresent, true, 'la saisie est conservée');
});

test('pointage : pas de rechargement pendant un enregistrement ni pendant la confirmation d\'abandon', async () => {
    for (const setBusy of [(t) => { t.saving = true; }, (t) => { t.discard = { open: true }; }, (t) => { t.showPdfModal = true; }]) {
        const { tab, count } = await bootAttendance();
        tab.loadedAt = 0;
        setBusy(tab);
        const before = count('/boarding/attendance?');

        await tab.onTabShown('pointage');

        assert.equal(count('/boarding/attendance?'), before);
    }
});

test('pointage : sans feuille affichée, l\'ouverture relit les pavillons (un pavillon créé entre-temps est proposé)', async () => {
    const { calls, count } = counter();
    let dormitories = [{ id: 'd1', name: 'P1' }, { id: 'd2', name: 'P2' }];
    const ctx = loadScripts(['internat-helpers.js', 'attendance-logic.js', 'internat-attendance.js'], {
        preload: { auth: AUTH('Directeur'), pdfPreview: PDF, api: api(calls, { '/boarding/dormitories': () => dormitories, '/boarding/attendance?': SHEET() }) }
    });
    ctx.window.internatHelpers.today = () => '2026-10-11';
    const tab = ctx.component('attendanceTab');
    await flush();
    assert.equal(tab.hasSheet, false, 'deux pavillons : rien n\'est choisi, aucune feuille');

    dormitories = [{ id: 'd1', name: 'P1' }, { id: 'd2', name: 'P2' }, { id: 'd3', name: 'P3' }];
    tab.loadedAt = 0;
    await tab.onTabShown('pointage');

    assert.equal(count('/boarding/dormitories'), 2);
    assert.deepEqual(plain(tab.dormitories.map((d) => d.id)), ['d1', 'd2', 'd3']);
});
