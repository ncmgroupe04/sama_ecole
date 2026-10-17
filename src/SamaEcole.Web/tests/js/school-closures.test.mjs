/**
 * Calendrier de fermeture (Franco-Arabe Bloc 5a) : logique pure (`window.schoolClosuresLogic`) et
 * composant `schoolClosuresView` (wwwroot/js/school-closures.js). Le serveur reste seul juge ; ces
 * tests prouvent que l'écran charge l'année ACTIVE, n'envoie que des corps bien formés (libellé
 * arabe vide → null, année dans l'URL) et garde la suppression en deux temps.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const YEARS = [
    { id: 'y1', label: '2025-2026', startDate: '2025-10-01', endDate: '2026-07-31', isActive: false, isClosed: true },
    { id: 'y2', label: '2026-2027', startDate: '2026-10-01', endDate: '2027-07-31', isActive: true, isClosed: false }
];

const TABASKI = { id: 'c1', schoolYearId: 'y2', startDate: '2027-06-06', endDate: '2027-06-06', kind: 'Holiday', label: 'Tabaski', labelAr: 'عيد الأضحى' };

function boot({ role = 'Directeur', closures = [TABASKI], failPost = null } = {}) {
    const calls = [];
    const ctx = loadScripts(['school-closures.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => {
                    calls.push(['GET', endpoint]);
                    if (endpoint === '/school-years') return YEARS;
                    if (endpoint.startsWith('/school-calendar/closures')) return closures;
                    return [];
                },
                post: async (endpoint, body) => {
                    calls.push(['POST', endpoint, plain(body)]);
                    if (failPost) throw failPost;
                    return {};
                },
                put: async (endpoint, body) => { calls.push(['PUT', endpoint, plain(body)]); return {}; },
                delete: async (endpoint) => { calls.push(['DELETE', endpoint]); return {}; },
                toMessage: (_err, fallback) => fallback,
                toFieldErrors: (err, fallback) => ({ global: err && err.message ? err.message : fallback })
            }
        }
    });
    return { ctx, calls };
}

const logic = () => boot().ctx.window.schoolClosuresLogic;

// -- Logique pure ------------------------------------------------------------------------------------

test('formatDate rend jj/MM/aaaa sans passer par Date', () => {
    assert.equal(logic().formatDate('2027-06-06'), '06/06/2027');
    assert.equal(logic().formatDate(''), '');
    assert.equal(logic().formatDate('pas-une-date'), 'pas-une-date');
});

test('formatRange : un jour isolé ou un intervalle', () => {
    assert.equal(logic().formatRange('2027-06-06', '2027-06-06'), '06/06/2027');
    assert.equal(logic().formatRange('2026-12-19', '2027-01-03'), '19/12/2026 → 03/01/2027');
});

test('dayCount compte les bornes incluses, à cheval sur deux années', () => {
    assert.equal(logic().dayCount('2027-06-06', '2027-06-06'), 1);
    assert.equal(logic().dayCount('2026-12-19', '2027-01-03'), 16);
});

test('validate : libellé, dates et bornes de l’année', () => {
    const year = YEARS[1];
    const ok = { label: 'Tabaski', labelAr: '', startDate: '2027-06-06', endDate: '2027-06-06' };

    assert.deepEqual(plain(logic().validate(ok, year)), {});
    assert.ok(logic().validate({ ...ok, label: '  ' }, year).label);
    assert.ok(logic().validate({ ...ok, startDate: '' }, year).startdate);
    assert.ok(logic().validate({ ...ok, endDate: '2027-06-01' }, year).enddate, 'fin avant début');
    assert.ok(logic().validate({ ...ok, startDate: '2028-01-01', endDate: '2028-01-02' }, year).startdate, 'hors année');
    assert.ok(logic().validate({ ...ok, labelAr: 'ع'.repeat(121) }, year).labelar);
});

// -- Composant ---------------------------------------------------------------------------------------

test('au chargement, l’année ACTIVE est sélectionnée et ses fermetures lues', async () => {
    const { ctx, calls } = boot();
    const view = ctx.component('schoolClosuresView');
    await flush();

    assert.equal(view.yearId, 'y2');
    assert.equal(view.closures.length, 1);
    assert.deepEqual(calls.at(-1), ['GET', '/school-calendar/closures?schoolYearId=y2']);
});

test('créer une fermeture : l’année est dans l’URL, un libellé arabe vide part en null', async () => {
    const { ctx, calls } = boot();
    const view = ctx.component('schoolClosuresView');
    await flush();

    view.openCreate();
    view.form = { kind: 'Vacation', startDate: '2026-12-19', endDate: '2027-01-03', label: ' Noël ', labelAr: '   ' };
    await view.submit();

    const post = calls.find((c) => c[0] === 'POST');
    assert.equal(post[1], '/school-calendar/closures?schoolYearId=y2');
    assert.deepEqual(post[2], { kind: 'Vacation', startDate: '2026-12-19', endDate: '2027-01-03', label: 'Noël', labelAr: null });
    assert.equal(view.isFormOpen, false, 'le formulaire se ferme après succès');
});

test('une saisie invalide n’appelle pas le serveur', async () => {
    const { ctx, calls } = boot();
    const view = ctx.component('schoolClosuresView');
    await flush();

    view.openCreate();
    view.form = { kind: 'Holiday', startDate: '2027-06-06', endDate: '2027-06-01', label: 'Tabaski', labelAr: '' };
    await view.submit();

    assert.equal(calls.some((c) => c[0] === 'POST'), false);
    assert.ok(view.formErrors.enddate);
    assert.equal(view.isFormOpen, true);
});

test('modifier : le formulaire est prérempli depuis une COPIE et part en PUT sur l’id', async () => {
    const { ctx, calls } = boot();
    const view = ctx.component('schoolClosuresView');
    await flush();

    view.openEdit(view.closures[0]);
    assert.equal(view.editingId, 'c1');
    assert.equal(view.form.labelAr, 'عيد الأضحى');

    view.form.label = 'Tabaski 2027';
    assert.equal(view.closures[0].label, 'Tabaski', 'la ligne du tableau n’est pas modifiée en direct');

    await view.submit();
    const put = calls.find((c) => c[0] === 'PUT');
    assert.equal(put[1], '/school-calendar/closures/c1');
    assert.equal(put[2].label, 'Tabaski 2027');
});

test('supprimer se fait en deux temps ; annuler n’envoie rien', async () => {
    const { ctx, calls } = boot();
    const view = ctx.component('schoolClosuresView');
    await flush();

    view.askDelete(view.closures[0]);
    assert.equal(view.pendingDeleteId, 'c1');
    view.cancelDelete();
    assert.equal(view.pendingDeleteId, null);
    assert.equal(calls.some((c) => c[0] === 'DELETE'), false);

    view.askDelete(view.closures[0]);
    await view.confirmDelete(view.closures[0]);
    assert.deepEqual(calls.find((c) => c[0] === 'DELETE'), ['DELETE', '/school-calendar/closures/c1']);
});

test('une erreur serveur est affichée dans le formulaire, qui reste ouvert', async () => {
    const { ctx } = boot({ failPost: new Error('Cette fermeture doit tenir dans l’année.') });
    const view = ctx.component('schoolClosuresView');
    await flush();

    view.openCreate();
    view.form = { kind: 'Holiday', startDate: '2027-06-06', endDate: '2027-06-06', label: 'Tabaski', labelAr: '' };
    await view.submit();

    assert.match(view.formErrors.global, /doit tenir/);
    assert.equal(view.isFormOpen, true);
    assert.equal(view.isSaving, false);
});

test('changer d’année recharge les fermetures de cette année et ferme le formulaire', async () => {
    const { ctx, calls } = boot();
    const view = ctx.component('schoolClosuresView');
    await flush();

    view.openCreate();
    view.yearId = 'y1';
    await view.loadClosures();

    assert.equal(view.isFormOpen, false);
    assert.deepEqual(calls.at(-1), ['GET', '/school-calendar/closures?schoolYearId=y1']);
});

test('onStartChanged recale la fin tant qu’elle est vide ou antérieure', async () => {
    const { ctx } = boot();
    const view = ctx.component('schoolClosuresView');
    await flush();

    view.openCreate();
    view.form.startDate = '2027-06-06';
    view.onStartChanged();
    assert.equal(view.form.endDate, '2027-06-06');

    view.form.endDate = '2027-06-10';
    view.form.startDate = '2027-06-07';
    view.onStartChanged();
    assert.equal(view.form.endDate, '2027-06-10', 'une fin postérieure est conservée');
});

test('hors Directeur, les actions d’écriture sont masquées (confort ; l’API répond 403)', async () => {
    const { ctx } = boot({ role: 'Secretariat' });
    const view = ctx.component('schoolClosuresView');
    await flush();

    assert.equal(view.isDirecteur, false);
});
