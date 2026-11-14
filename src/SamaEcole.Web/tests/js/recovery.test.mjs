/**
 * Écran Recouvrement (`recoveryView`, wwwroot/js/recovery.js) — lots de relance SMS des débiteurs.
 * Le serveur décide des destinataires et du texte ; l'écran charge, confirme et relaie, sans jamais envoyer
 * un lot sans geste explicite ni deux fois.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

// Les objets créés dans le contexte vm n'ont pas les mêmes prototypes : on compare leur sérialisation.
const same = (actual, expected) => assert.equal(JSON.stringify(actual), JSON.stringify(expected));

function makeView(api = {}) {
    const calls = [];
    const ctx = loadScripts(['recovery.js'], {
        preload: {
            auth: { role: 'Finance' },
            api: {
                get: async (url) => { calls.push(['get', url]); return api.get ? api.get(url) : []; },
                post: async (url, body) => { calls.push(['post', url, body]); return api.post ? api.post(url, body) : null; },
                toMessage: (err, fallback) => (err && err.message) || fallback
            }
        }
    });
    const view = ctx.component('recoveryView');
    // Le composant se charge dès sa création (init) : on repart d'un journal d'appels vierge.
    calls.length = 0;
    view.calls = calls;
    return view;
}

const BATCH = {
    id: 'b1', classroomName: 'CM2 A', thresholdDays: 15, status: 'Draft', generatedAt: '2026-11-14T02:00:00Z', sentAt: null,
    items: [
        { enrollmentId: 'e1', studentFullName: 'Awa Diop', matricule: 'ELEV-1', guardianPhone: '770000000', remainingBalance: 25000, daysOverdue: 30 },
        { enrollmentId: 'e2', studentFullName: 'Moussa Fall', matricule: 'ELEV-2', guardianPhone: null, remainingBalance: 10000, daysOverdue: 20 },
        { enrollmentId: 'e3', studentFullName: 'Fatou Sow', matricule: 'ELEV-3', guardianPhone: '  ', remainingBalance: 5000, daysOverdue: 16 }
    ]
};

test('charge par défaut les lots à valider (brouillons)', async () => {
    const view = makeView({ get: async () => [BATCH] });
    await view.load();

    same(view.calls[0], ['get', '/finance/dues-reminder-batches?status=Draft']);
    assert.equal(view.batches.length, 1);
});

test('le filtre « Tous » n\'envoie aucun paramètre de statut', async () => {
    const view = makeView();
    await view.selectStatus('');

    same(view.calls[0], ['get', '/finance/dues-reminder-batches']);
});

test('total du lot et élèves sans numéro de tuteur', () => {
    const view = makeView();

    assert.equal(view.batchTotal(BATCH), 40000);
    assert.equal(view.withoutPhone(BATCH), 2);
    assert.equal(view.phoneLabel(BATCH.items[1]), 'Aucun numéro');
    assert.equal(view.phoneLabel(BATCH.items[0]), '770000000');
});

test('seul un brouillon propose l\'envoi', () => {
    const view = makeView();

    assert.equal(view.isDraft(BATCH), true);
    assert.equal(view.isDraft({ ...BATCH, status: 'Sent' }), false);
    assert.equal(view.statusLabel('Sent'), 'Envoyé');
});

test('rien ne part avant la confirmation : ouvrir la fenêtre n\'appelle pas le serveur', () => {
    const view = makeView();
    view.openSend(BATCH);

    assert.equal(view.sendTarget, BATCH);
    assert.equal(view.calls.filter((c) => c[0] === 'post').length, 0);
});

test('confirmer envoie le lot une fois, annonce le résultat et recharge', async () => {
    const view = makeView({
        post: async () => ({ queuedCount: 1, skippedCount: 2, firstSkipReason: 'Numéro manquant' }),
        get: async () => []
    });
    view.openSend(BATCH);
    await view.confirmSend();

    same(view.calls[0], ['post', '/finance/dues-reminder-batches/b1/send', {}]);
    assert.match(view.notice, /1 SMS mis en file/);
    assert.match(view.notice, /2 écarté\(s\) \(Numéro manquant\)/);
    assert.equal(view.sendTarget, null);
});

test('un envoi refusé (409) garde la fenêtre ouverte avec le message et recharge l\'état réel', async () => {
    const conflict = Object.assign(new Error('Ce lot a déjà été envoyé.'), { status: 409 });
    const view = makeView({ post: async () => { throw conflict; }, get: async () => [] });
    view.openSend(BATCH);
    await view.confirmSend();

    assert.equal(view.sendError, 'Ce lot a déjà été envoyé.');
    assert.equal(view.sendTarget, BATCH);
    assert.ok(view.calls.some((c) => c[0] === 'get'), 'le lot est relu après le 409');
});

test('écarter appelle dismiss puis recharge', async () => {
    const view = makeView({ post: async () => null, get: async () => [] });
    view.openDismiss(BATCH);
    await view.confirmDismiss();

    same(view.calls[0], ['post', '/finance/dues-reminder-batches/b1/dismiss', {}]);
    assert.equal(view.dismissTarget, null);
    assert.match(view.notice, /écarté/);
});

test('recalculer annonce le nombre de lots préparés', async () => {
    const view = makeView({ post: async () => 3, get: async () => [] });
    await view.recompute();

    same(view.calls[0], ['post', '/finance/dues-reminder-batches/recompute', {}]);
    assert.match(view.notice, /3 lot\(s\)/);
});

test('une erreur de chargement vide la liste et affiche le message', async () => {
    const view = makeView({ get: async () => { throw new Error('Accès refusé'); } });
    await view.load();

    assert.equal(view.error, 'Accès refusé');
    same(view.batches, []);
});
