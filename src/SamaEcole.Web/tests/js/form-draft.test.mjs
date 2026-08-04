/**
 * Brouillons de formulaire conservés sur le poste (wwwroot/js/form-draft.js).
 *
 * Deux propriétés sont non négociables : aucun secret ne doit atterrir en clair dans localStorage,
 * et un brouillon de l'école A ne doit jamais reparaître dans l'école B — le préfixe par SchoolId
 * est le seul cloisonnement qui existe côté navigateur, la RLS PostgreSQL ne protégeant rien ici.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

function bootDrafts(schoolId = 'ecole-a') {
    const ctx = loadScripts(['form-draft.js'], {
        preload: { auth: { schoolId } }
    });
    return ctx;
}

test('un brouillon sauvegardé se relit à l\'identique', () => {
    const ctx = bootDrafts();

    ctx.window.formDraft.save('inscription', { fullName: 'Awa Diop', classroomId: 42 });

    assert.deepEqual(plain(ctx.window.formDraft.load('inscription')), { fullName: 'Awa Diop', classroomId: 42 });
    assert.equal(ctx.window.formDraft.has('inscription'), true);
});

test('les champs sensibles ne sont jamais écrits sur le poste', () => {
    const ctx = bootDrafts();

    ctx.window.formDraft.save('utilisateur', {
        email: 'awa@ecole.sn',
        password: 'secret',
        confirmPassword: 'secret',
        contact: { pin: '1234', phone: '770000000' }
    });

    const restored = ctx.window.formDraft.load('utilisateur');
    assert.deepEqual(plain(restored), { email: 'awa@ecole.sn', contact: { phone: '770000000' } });

    // Vérification au niveau du stockage brut : rien ne doit transparaître, même sérialisé.
    const raw = ctx.localStorage.getItem('draft_samaecole_ecole-a_utilisateur');
    assert.ok(!raw.includes('secret'));
    assert.ok(!raw.includes('1234'));
});

test('un brouillon de l\'école A est invisible depuis l\'école B', () => {
    const ctx = bootDrafts('ecole-a');
    ctx.window.formDraft.save('caisse', { amount: 25000 });
    assert.equal(ctx.window.formDraft.count(), 1);

    // Bascule d'établissement (school-switcher.js) : même navigateur, même localStorage.
    ctx.window.auth.schoolId = 'ecole-b';

    assert.equal(ctx.window.formDraft.count(), 0);
    assert.equal(ctx.window.formDraft.load('caisse'), null);
});

test('count() ignore les brouillons expirés sans sauter les suivants', () => {
    const ctx = bootDrafts();
    const vieux = new Date(Date.now() - 25 * 60 * 60 * 1000).toISOString();

    // Deux entrées périmées EN TÊTE : c'est le cas qui piège un parcours indexé de localStorage,
    // puisque chaque purge décale les index restants.
    ctx.localStorage.setItem('draft_samaecole_ecole-a_perime1', JSON.stringify({ savedAt: vieux, payload: { a: 1 } }));
    ctx.localStorage.setItem('draft_samaecole_ecole-a_perime2', JSON.stringify({ savedAt: vieux, payload: { a: 2 } }));
    ctx.window.formDraft.save('valide1', { a: 3 });
    ctx.window.formDraft.save('valide2', { a: 4 });

    assert.equal(ctx.window.formDraft.count(), 2);
    assert.deepEqual(plain(ctx.window.formDraft.list().map((d) => d.key).sort()), ['valide1', 'valide2']);

    // Les périmés ont bien été purgés du stockage, pas seulement filtrés de l'inventaire.
    assert.equal(ctx.localStorage.getItem('draft_samaecole_ecole-a_perime1'), null);
    assert.equal(ctx.localStorage.getItem('draft_samaecole_ecole-a_perime2'), null);
});

test('clear() purge le brouillon et signale le changement', () => {
    const ctx = bootDrafts();
    let notifications = 0;
    ctx.window.addEventListener('samaecole:drafts-changed', () => { notifications++; });

    ctx.window.formDraft.save('inscription', { fullName: 'Awa Diop' });
    ctx.window.formDraft.clear('inscription');

    assert.equal(ctx.window.formDraft.count(), 0);
    assert.equal(notifications, 2, 'une notification à l\'écriture, une à la purge');
});

test('un stockage illisible ne fait pas planter l\'inventaire', () => {
    const ctx = bootDrafts();
    ctx.localStorage.setItem('draft_samaecole_ecole-a_corrompu', 'ceci-n-est-pas-du-json');
    ctx.window.formDraft.save('valide', { a: 1 });

    assert.equal(ctx.window.formDraft.count(), 1);
});
