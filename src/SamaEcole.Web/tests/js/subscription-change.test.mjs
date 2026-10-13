/**
 * Verrou d'abonnement côté interface : le pont api.js → modale universelle « Mise à jour de votre abonnement »
 * (wwwroot/js/api.js, subscription-change.js), l'envoi de la demande, et la remise à l'état d'origine des réglages quand un
 * enregistrement est intercepté (settings.js).
 *
 * Ce que ces tests protègent : une modification directe interceptée par le serveur (409 SUBSCRIPTION_CHANGE_REQUIRED) n'écrit RIEN
 * et ne s'affiche PAS comme un bandeau rouge d'écran — elle ouvre la modale avec l'aperçu et le corps de la demande, que le
 * Directeur envoie tel quel ; les autres rôles voient l'aperçu sans pouvoir l'envoyer.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const PREVIEW = () => ({
    hasChanges: true,
    isDowngrade: false,
    message: 'Pour basculer vers ce profil ou ajouter ces options/effectifs, une mise à jour de votre abonnement est nécessaire (tarif ajusté).',
    changes: [{ field: 'Tier', from: "jusqu'à 150 élèves", to: "jusqu'à 250 élèves" }],
    problems: [],
    current: { monthlyXof: 10000, annualXof: 100000, requiresQuote: false, known: true },
    target: { monthlyXof: 15000, annualXof: 150000, requiresQuote: false, known: true },
    differenceMonthlyXof: 5000,
    differenceAnnualXof: 50000,
    hasPendingRequest: false,
    request: { tier: 'Tier_250' },
    canRequest: true
});

function bootStore({ role = 'Directeur', post } = {}) {
    const calls = [];
    const ctx = loadScripts(['subscription-change.js'], {
        preload: {
            auth: { role },
            api: {
                post: async (endpoint, body) => {
                    calls.push({ endpoint, body: plain(body) });
                    return post ? post(endpoint, body) : { id: 'req-1', status: 'Pending' };
                },
                toMessage: (err, fallback) => (err && err.handledGlobally ? '' : fallback)
            }
        }
    });
    ctx.initAlpine();
    return { ctx, store: ctx.store('subscriptionChange'), calls };
}

test("un événement d'interception ouvre la modale avec le message et l'aperçu du serveur", () => {
    const { ctx, store } = bootStore();

    ctx.window.emit('sama:subscription-change', { message: 'Mise à jour nécessaire', preview: PREVIEW() });

    assert.equal(store.open, true);
    assert.equal(store.message, 'Mise à jour nécessaire');
    assert.equal(store.preview.differenceMonthlyXof, 5000);
    assert.equal(store.sentRequest, null);
});

test('le Directeur envoie la demande avec le corps renvoyé par le serveur, et la confirmation remplace l\'aperçu', async () => {
    const { ctx, store, calls } = bootStore();
    ctx.window.emit('sama:subscription-change', { message: 'm', preview: PREVIEW() });
    store.note = '  Nous ouvrons un internat en janvier.  ';

    assert.equal(store.canSend, true);
    await store.send();

    assert.equal(calls.length, 1);
    assert.equal(calls[0].endpoint, '/tenant-subscription/change-requests');
    assert.deepEqual(calls[0].body.change, { tier: 'Tier_250' }, 'le corps est celui de l\'aperçu, jamais reconstruit ici');
    assert.equal(calls[0].body.note, 'Nous ouvrons un internat en janvier.');
    assert.equal(store.sentRequest.id, 'req-1');
    assert.equal(store.canSend, false, 'une demande envoyée ne se renvoie pas');
});

test("un autre rôle voit l'aperçu mais ne peut pas envoyer la demande", async () => {
    const { ctx, store, calls } = bootStore({ role: 'Secretariat' });
    ctx.window.emit('sama:subscription-change', { message: 'm', preview: PREVIEW() });

    assert.equal(store.isDirector, false);
    assert.equal(store.canSend, false);
    await store.send();

    assert.equal(calls.length, 0);
});

test("une demande déjà en cours ou bloquée par un problème ne peut pas partir", async () => {
    const { ctx, store, calls } = bootStore();
    const pending = { ...PREVIEW(), hasPendingRequest: true, canRequest: false };
    ctx.window.emit('sama:subscription-change', { message: 'm', preview: pending });

    assert.equal(store.canSend, false);
    await store.send();
    assert.equal(calls.length, 0);

    ctx.window.emit('sama:subscription-change', {
        message: 'm', preview: { ...PREVIEW(), problems: ['Le cycle Collège compte 3 classes.'], canRequest: false }
    });
    assert.equal(store.hasProblems, true);
    assert.equal(store.canSend, false);
});

test('un échec d\'envoi reste dans la modale, sans la fermer, et la demande n\'est pas marquée envoyée', async () => {
    const { ctx, store } = bootStore({ post: async () => { throw new Error('boom'); } });
    ctx.window.emit('sama:subscription-change', { message: 'm', preview: PREVIEW() });

    await store.send();

    assert.equal(store.open, true);
    assert.equal(store.sentRequest, null);
    assert.match(store.error, /Impossible d'envoyer la demande/);
    assert.equal(store.isSending, false);
});

test("l'écart de tarif, le tarif seul et le devis se distinguent pour l'affichage", () => {
    const { ctx, store } = bootStore();

    ctx.window.emit('sama:subscription-change', { message: 'm', preview: PREVIEW() });
    assert.equal(store.hasPriceDifference, true);
    assert.equal(store.hasTargetPriceOnly, false);

    ctx.window.emit('sama:subscription-change', {
        message: 'm',
        preview: { ...PREVIEW(), differenceMonthlyXof: null, differenceAnnualXof: null, current: { known: false } }
    });
    assert.equal(store.hasPriceDifference, false);
    assert.equal(store.hasTargetPriceOnly, true, 'école encore sur l\'ancienne grille : tarif cible sans écart');

    ctx.window.emit('sama:subscription-change', {
        message: 'm',
        preview: { ...PREVIEW(), differenceMonthlyXof: null, target: { requiresQuote: true, known: false } }
    });
    assert.equal(store.isOnQuote, true);
    assert.equal(store.hasTargetPriceOnly, false);
});

test('le signe d\'un écart de tarif se lit', () => {
    const { store } = bootStore();

    assert.match(store.formatSigned(5000), /^\+/);
    assert.match(store.formatSigned(-2000), /^−/);
    assert.equal(store.formatSigned(null), '—');
});

test('requestChange demande l\'aperçu au serveur puis ouvre la modale (jamais de tarif calculé ici)', async () => {
    const { store, calls } = bootStore({ post: async () => PREVIEW() });

    await store.requestChange({ tier: 'Tier_250' });

    assert.deepEqual(calls[0], { endpoint: '/tenant-subscription/change-requests/preview', body: { tier: 'Tier_250' } });
    assert.equal(store.open, true);
    assert.equal(store.preview.target.monthlyXof, 15000);
    assert.equal(store.isLoading, false);
});

test("requestChange en échec ouvre la modale avec le message d'erreur", async () => {
    const { store } = bootStore({ post: async () => { throw new Error('réseau'); } });

    await store.requestChange({ tier: 'Tier_250' });

    assert.equal(store.open, true);
    assert.equal(store.preview, null);
    assert.match(store.error, /Impossible de préparer la demande/);
});

// ---------------------------------------------------------------- Pont api.js

test('api.js ouvre la modale sur un 409 SUBSCRIPTION_CHANGE_REQUIRED et marque l\'erreur comme déjà traitée', async () => {
    const fetchStub = async () => ({
        ok: false,
        status: 409,
        json: async () => ({ code: 'SUBSCRIPTION_CHANGE_REQUIRED', message: 'Mise à jour nécessaire', details: PREVIEW() })
    });
    const ctx = loadScripts(['api.js'], {
        fetch: fetchStub,
        preload: { auth: { accessToken: 'jwt', isAuthenticated: () => false, isAccessTokenStale: () => false, redirectToLogin() {} } }
    });
    const events = [];
    ctx.window.addEventListener('sama:subscription-change', (event) => events.push(event.detail));

    let caught;
    try {
        await ctx.window.api.put('/schools/current/settings/managed-cycles', { cycles: ['Primaire'] });
    } catch (err) {
        caught = err;
    }

    assert.equal(events.length, 1);
    assert.equal(events[0].message, 'Mise à jour nécessaire');
    assert.equal(events[0].preview.request.tier, 'Tier_250');
    assert.equal(caught.code, 'SUBSCRIPTION_CHANGE_REQUIRED');
    assert.equal(caught.handledGlobally, true);
    assert.equal(ctx.window.api.toMessage(caught, 'repli'), '', 'aucun bandeau d\'erreur en plus de la modale');
});

test("un autre 409 (règle métier) n'ouvre pas la modale d'abonnement", async () => {
    const fetchStub = async () => ({
        ok: false, status: 409, json: async () => ({ code: 'CYCLE_HAS_CLASSROOMS', message: 'Le cycle compte 2 classes.' })
    });
    const ctx = loadScripts(['api.js'], {
        fetch: fetchStub,
        preload: { auth: { accessToken: 'jwt', isAuthenticated: () => false, isAccessTokenStale: () => false, redirectToLogin() {} } }
    });
    const events = [];
    ctx.window.addEventListener('sama:subscription-change', (event) => events.push(event));

    await assert.rejects(() => ctx.window.api.put('/schools/current/settings/managed-cycles', { cycles: [] }));

    assert.equal(events.length, 0);
});

// ---------------------------------------------------------------- Paramètres : retour à l'état d'origine

test('un enregistrement intercepté remet les interrupteurs de modules dans leur état réel', async () => {
    const server = {
        gradingScale: '20', studentMatriculeFormat: 'ELEV-{YEAR}-{SEQ:4}', teacherMatriculeFormat: 'ENS-{YEAR}-{SEQ:3}',
        autoLogoutMinutes: 10, dateFormat: 'dd/MM/yyyy', tuitionMonthsPerYear: 9, allowSecretaryToManageGrading: false,
        allowFinanceToModifyFees: false, allowFinanceToDeleteFees: false, typeEtablissement: 'Prive',
        isPedagogyEnabled: true, isFinanceEnabled: true, isInternatEnabled: false, isCoranModuleEnabled: false,
        gradeEditWindowDays: 7, evaluationPeriodType: 'Trimester', customPeriodCount: 3
    };
    const intercepted = Object.assign(new Error('Mise à jour nécessaire'), {
        code: 'SUBSCRIPTION_CHANGE_REQUIRED', status: 409, handledGlobally: true
    });
    const ctx = loadScripts(['settings.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/settings') return server;
                    if (endpoint === '/schools/current/mode') return { isLive: false };
                    if (endpoint === '/schools/current') return { name: 'École test' };
                    return [];
                },
                put: async () => { throw intercepted; },
                toMessage: (e, fallback) => (e && e.handledGlobally ? '' : fallback),
                toFieldErrors: (e, fallback) => (e && e.handledGlobally ? {} : { global: fallback })
            },
            location: { href: 'https://localhost/parametres', search: '' },
            history: { replaceState() {} }
        }
    });
    const view = ctx.component('settingsView');
    await flush();
    assert.equal(view.config.isInternatEnabled, false);

    view.config.isInternatEnabled = true; // le Directeur bascule l'interrupteur
    await view.saveConfig();

    assert.equal(view.config.isInternatEnabled, false, 'le module n\'est pas souscrit : l\'interrupteur revient à son état réel');
    assert.deepEqual(plain(view.configErrors), {}, 'aucune erreur locale en plus de la modale');
});
