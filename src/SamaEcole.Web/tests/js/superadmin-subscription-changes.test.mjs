/**
 * Console Super Admin › Changements d'abonnement (wwwroot/js/superadmin-subscription-changes.js).
 *
 * L'écran n'écrit jamais l'état d'une école : « Valider » demande au serveur d'appliquer l'état cible stocké sur la demande, après
 * ses propres gardes. Ces tests prouvent le chargement des deux listes, le filtre par statut, la décision (motif obligatoire pour un
 * rejet, corps envoyé, rechargement) et que le refus d'une garde serveur (409) reste dans la modale, la demande restant en attente.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const REQUEST = (overrides = {}) => ({
    id: 'req-1',
    schoolId: 'school-1',
    schoolName: 'École Les Baobabs',
    status: 'Pending',
    isDowngrade: false,
    changes: [{ field: 'Tier', from: "jusqu'à 150 élèves", to: "jusqu'à 250 élèves" }],
    currentMonthlyXof: 10000,
    currentAnnualXof: 100000,
    targetMonthlyXof: 15000,
    targetAnnualXof: 150000,
    differenceMonthlyXof: 5000,
    differenceAnnualXof: 50000,
    directorNote: null,
    reviewNote: null,
    createdAt: '2026-10-13T08:00:00Z',
    reviewedAt: null,
    ...overrides
});

const OVER_QUOTA = () => ({
    schoolId: 'school-2', schoolName: 'Daara Touba', tier: 'Tier_100', maxStudentLimit: 100, softQuotaLimit: 110,
    studentCount: 112, state: 'Exceeded', overQuotaSince: '2026-10-01T08:00:00Z'
});

async function boot({ requests = [REQUEST()], overQuota = [OVER_QUOTA()], post } = {}) {
    const gets = [];
    const posts = [];
    const ctx = loadScripts(['superadmin-subscription-changes.js'], {
        preload: {
            api: {
                get: async (endpoint) => {
                    gets.push(endpoint);
                    return endpoint.startsWith('/admin/over-quota-schools') ? overQuota : requests;
                },
                post: async (endpoint, body) => {
                    posts.push({ endpoint, body: plain(body) });
                    if (post) return post(endpoint, body);
                    return {};
                },
                toMessage: (err, fallback) => (err && err.message) || fallback
            }
        }
    });
    const page = ctx.initAlpine().get('superAdminSubscriptionChanges')();
    await page.load();
    return { page, gets, posts };
}

test('charge les demandes en attente et les écoles en dépassement', async () => {
    const { page, gets } = await boot();

    assert.deepEqual(gets.sort(), ['/admin/over-quota-schools', '/admin/subscription-change-requests?status=Pending']);
    assert.equal(page.requests.length, 1);
    assert.equal(page.overQuota.length, 1);
    assert.equal(page.pendingCount, 1);
    assert.equal(page.error, null);
});

test('le filtre par statut recharge la liste, « Toutes » ne filtre pas', async () => {
    const { page, gets } = await boot();

    await page.selectStatus('Approved');
    assert.equal(gets.at(-1), '/admin/subscription-change-requests?status=Approved');

    await page.selectStatus('');
    assert.equal(gets.at(-1), '/admin/subscription-change-requests');
});

test("la ligne de tarif distingue l'écart connu, le tarif seul et le tarif à établir", async () => {
    const { page } = await boot();

    assert.match(page.priceLine(REQUEST()), /\+.*10[\s ]?000|\+.*5[\s ]000/);
    assert.match(page.priceLine(REQUEST()), /\/ mois .* \/ an/);
    assert.match(page.priceLine(REQUEST({ differenceMonthlyXof: null, differenceAnnualXof: null })), /écart à chiffrer/);
    assert.match(page.priceLine(REQUEST({ differenceMonthlyXof: null, targetMonthlyXof: null })), /à établir/);
});

test('valider envoie la décision puis recharge ; la note est facultative', async () => {
    const { page, posts, gets } = await boot();

    page.openDecision(page.requests[0], 'approve');
    assert.equal(page.decisionTitle, 'Valider la demande — École Les Baobabs');
    page.decisionNote = '  Règlement reçu.  ';
    await page.confirmDecision();

    assert.deepEqual(posts[0], { endpoint: '/admin/subscription-change-requests/req-1/approve', body: { note: 'Règlement reçu.' } });
    assert.equal(page.decisionTarget, null, 'la modale se ferme');
    assert.ok(gets.filter((g) => g.startsWith('/admin/subscription-change-requests')).length >= 2, 'la liste est rechargée');
});

test('rejeter exige un motif : rien ne part sans lui', async () => {
    const { page, posts } = await boot();

    page.openDecision(page.requests[0], 'reject');
    page.decisionNote = '   ';
    await page.confirmDecision();

    assert.equal(posts.length, 0);
    assert.match(page.decisionError, /motif du rejet/);
    assert.notEqual(page.decisionTarget, null);

    page.decisionNote = 'Règlement non reçu.';
    await page.confirmDecision();

    assert.deepEqual(posts[0], { endpoint: '/admin/subscription-change-requests/req-1/reject', body: { note: 'Règlement non reçu.' } });
});

test("un refus du serveur (état périmé, effectif, classes) reste dans la modale et laisse la demande en attente", async () => {
    const { page } = await boot({
        post: async () => { throw new Error("L'abonnement de cette école a changé depuis la demande."); }
    });

    page.openDecision(page.requests[0], 'approve');
    await page.confirmDecision();

    assert.match(page.decisionError, /a changé depuis la demande/);
    assert.notEqual(page.decisionTarget, null, 'la modale reste ouverte pour lire la raison');
    assert.equal(page.isDeciding, false);
});

test('la décision dit si le Directeur a été prévenu par e-mail, et alerte sinon', async () => {
    const notified = await boot({ post: async () => ({ status: 'Approved', directorNotified: true }) });
    notified.page.openDecision(notified.page.requests[0], 'approve');
    await notified.page.confirmDecision();
    assert.match(notified.page.notice, /prévenu par e-mail/);
    assert.equal(notified.page.noticeIsWarning, false);

    const notNotified = await boot({ post: async () => ({ status: 'Rejected', directorNotified: false }) });
    notNotified.page.openDecision(notNotified.page.requests[0], 'reject');
    notNotified.page.decisionNote = 'Règlement non reçu.';
    await notNotified.page.confirmDecision();
    assert.match(notNotified.page.notice, /n'a pas pu être envoyé/);
    assert.equal(notNotified.page.noticeIsWarning, true);
});

test("l'état d'une école en dépassement se lit : tolérance entamée ou création bloquée", async () => {
    const { page } = await boot();

    assert.match(page.overQuotaStateLabel('InTolerance'), /tolérance/i);
    assert.match(page.overQuotaStateLabel('Exceeded'), /bloquée/);
    assert.match(page.overQuotaStateClasses('Exceeded'), /rose/);
    assert.match(page.overQuotaStateClasses('InTolerance'), /amber/);
});

test("la tranche d'une école s'affiche en clair, jamais sous son identifiant d'API", async () => {
    const { page } = await boot();

    assert.equal(page.tierLabel('Tier_100'), "jusqu'à 100 élèves");
    assert.equal(page.tierLabel('Tier2_400'), "jusqu'à 400 élèves");
    assert.match(page.tierLabel('Tier3_800'), /Grand Groupe/);
    assert.equal(page.tierLabel('Inconnue'), 'Inconnue', 'une valeur inconnue reste lisible plutôt que vide');
});

test('un échec de chargement affiche un message et ne casse pas la page', async () => {
    const ctx = loadScripts(['superadmin-subscription-changes.js'], {
        preload: { api: { get: async () => { throw new Error('boom'); }, toMessage: (_e, fallback) => fallback } }
    });
    const page = ctx.initAlpine().get('superAdminSubscriptionChanges')();

    await page.load();

    assert.match(page.error, /Erreur lors du chargement/);
    assert.equal(page.isLoading, false);
});
