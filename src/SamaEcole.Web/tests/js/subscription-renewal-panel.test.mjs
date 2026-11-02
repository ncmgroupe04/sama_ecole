/**
 * Bouton « Renouveler mon abonnement » de l'onglet Facturation & historique (wwwroot/js/subscription-payments-history.js).
 *
 * Règles verrouillées ici : seul le Directeur voit le bouton, et seulement pour un abonnement Actif ou en lecture seule ;
 * l'état « échu » vaut aussi pour un abonnement encore marqué Actif dont l'échéance est dépassée (le contrôle quotidien n'a
 * peut-être pas tourné) ; le message prévient quand l'échéance approche ; le lien mène à l'écran de paiement existant.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

/** 'AAAA-MM-JJ' à N jours d'aujourd'hui, en date calendaire LOCALE (comme le composant). */
function dateInDays(offset) {
    const d = new Date();
    d.setDate(d.getDate() + offset);
    const pad = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * Le composant charge l'historique dès sa création (Directeur) : l'API doublée renvoie donc directement l'abonnement voulu,
 * puis on attend la fin du chargement avant d'interroger l'état.
 */
async function view({ role = 'Directeur', status = 'Active', expiresInDays = 90 } = {}) {
    const api = {
        get: async () => ({
            items: [], totalCount: 0,
            subscriptionStatus: status,
            subscriptionPlan: 'Standard',
            subscriptionExpiresAt: expiresInDays === null ? null : dateInDays(expiresInDays)
        }),
        toMessage: (err, fallback) => fallback
    };
    const ctx = loadScripts(['subscription-payments-history.js'], { preload: { auth: { role, schoolId: 'school-1' }, api } });
    const state = ctx.component('subscriptionPaymentsHistoryView');
    // Un rôle autre que Directeur ne charge rien : on pose alors l'état à la main, comme le ferait une réponse de l'API.
    if (!state.isDirecteur) {
        state.subscriptionStatus = status;
        state.subscriptionExpiresAt = expiresInDays === null ? null : dateInDays(expiresInDays);
    }
    await flush();
    return state;
}

test('seul le Directeur voit le bouton de renouvellement', async () => {
    assert.equal((await view({ role: 'Directeur' })).canRenew, true);
    assert.equal((await view({ role: 'Secretariat' })).canRenew, false);
    assert.equal((await view({ role: 'Finance' })).canRenew, false);
});

test('le bouton s\'affiche pour un abonnement Actif ou en lecture seule, jamais pour Suspendu ni En attente de paiement', async () => {
    assert.equal((await view({ status: 'Active' })).canRenew, true);
    assert.equal((await view({ status: 'ReadOnly' })).canRenew, true);
    assert.equal((await view({ status: 'Suspended' })).canRenew, false, 'un abonnement suspendu se règle avec le Super Admin');
    assert.equal((await view({ status: 'AwaitingPayment' })).canRenew, false, 'le premier paiement a son propre écran');
});

test('un abonnement en lecture seule est échu', async () => {
    const state = await view({ status: 'ReadOnly', expiresInDays: -3 });
    assert.equal(state.isExpired, true);
    assert.match(state.renewalMessage, /a expiré/);
});

test('un abonnement encore marqué Actif mais dont l\'échéance est dépassée est traité comme échu', async () => {
    // Le serveur applique déjà la lecture seule dans ce cas (SubscriptionAwaitingPaymentMiddleware) : l'écran doit le dire.
    const state = await view({ status: 'Active', expiresInDays: -2 });
    assert.equal(state.isExpired, true);
    assert.equal(state.isExpiringSoon, false);
});

test('le jour même de l\'échéance, l\'abonnement n\'est pas encore échu (comme le serveur : échéance < aujourd\'hui)', async () => {
    const state = await view({ status: 'Active', expiresInDays: 0 });
    assert.equal(state.isExpired, false);
    assert.equal(state.isExpiringSoon, true);
    assert.match(state.renewalMessage, /expire aujourd'hui/);
});

test('à moins de 30 jours de l\'échéance, le message prévient et rassure sur le temps restant', async () => {
    const state = await view({ status: 'Active', expiresInDays: 10 });
    assert.equal(state.isExpiringSoon, true);
    assert.match(state.renewalMessage, /dans 10 jours/);
    assert.match(state.renewalMessage, /s'ajoute au temps restant/);

    assert.match((await view({ expiresInDays: 1 })).renewalMessage, /demain/);
});

test('loin de l\'échéance, le renouvellement à l\'avance reste proposé, sans alarme', async () => {
    const state = await view({ status: 'Active', expiresInDays: 120 });
    assert.equal(state.isExpired, false);
    assert.equal(state.isExpiringSoon, false);
    assert.match(state.renewalMessage, /renouveler à l'avance/);
});

test('sans échéance connue, rien n\'est signalé comme échu', async () => {
    const state = await view({ status: 'Active', expiresInDays: null });
    assert.equal(state.isExpired, false);
    assert.equal(state.isExpiringSoon, false);
});

test('le bouton mène à l\'écran de paiement existant, en mode renouvellement', async () => {
    assert.equal((await view()).renewUrl, '/abonnement/paiement?renouvellement=1');
});
