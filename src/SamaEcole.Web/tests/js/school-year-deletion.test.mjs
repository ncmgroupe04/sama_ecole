/**
 * Écran Années scolaires (`schoolYearsView`, wwwroot/js/school-years.js) — SUPPRESSION d'une année.
 *
 * Deux choses seulement sont vérifiées ici, mais ce sont celles qui tiennent la garde côté écran :
 *
 *   1. `deleteConfirmationMatches` doit se comporter EXACTEMENT comme
 *      SchoolYearDeletionConfirmation.Matches côté serveur (tests unitaires C# du même nom) — c'est
 *      lui qui déverrouille le bouton rouge. Une divergence entre les deux donnerait soit un bouton
 *      actif que l'API refuse ensuite en 422, soit un bouton mort sur une saisie pourtant correcte ;
 *
 *   2. le RÉGIME de l'établissement (mode test / mode réel) est lu au chargement, car il change ce
 *      que la modale ANNONCE : effacement définitif d'un côté, archivage conditionnel de l'autre.
 *      En cas d'échec de cette lecture, le défaut doit être le moins destructeur des deux.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

const YEAR = {
    id: 'y-1', label: '2025-2026', startDate: '2025-10-01', endDate: '2026-07-31',
    isActive: false, isClosed: true
};

/**
 * Instancie le composant. `mode` est la réponse de GET /schools/current/mode ; `modeFails` simule
 * une lecture impossible (réseau coupé), pour vérifier le défaut prudent.
 */
async function schoolYearsView({ mode = { isLive: false }, modeFails = false } = {}) {
    const ctx = loadScripts(['school-years.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/mode') {
                        if (modeFails) throw new Error('réseau');
                        return mode;
                    }
                    return [YEAR];
                },
                delete: async () => ({}),
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            }
        }
    });

    const view = ctx.component('schoolYearsView');
    await flush();
    return view;
}

test('le bouton ne se déverrouille que sur le libellé exact de l\'année visée', async () => {
    const view = await schoolYearsView();
    view.openDelete(YEAR);

    for (const typed of ['', '   ', '2025', '20252026', '2025-2027', 'PURGER']) {
        view.deleteConfirmation = typed;
        assert.equal(view.deleteConfirmationMatches, false, `« ${typed} » ne doit rien déverrouiller`);
    }

    view.deleteConfirmation = '2025-2026';
    assert.equal(view.deleteConfirmationMatches, true);
});

test('les espaces de bord et la casse sont tolérés, comme côté serveur', async () => {
    const view = await schoolYearsView();
    view.openDelete({ ...YEAR, label: 'Année A' });

    // Le libellé se recopie à la main : c'est la RECOPIE qui fait la garde, pas la graphie.
    for (const typed of ['  Année A  ', 'année a', 'ANNÉE A']) {
        view.deleteConfirmation = typed;
        assert.equal(view.deleteConfirmationMatches, true, `« ${typed} » doit être accepté`);
    }
});

test('aucune année ouverte : rien ne se déverrouille', async () => {
    const view = await schoolYearsView();

    // yearToDelete est null tant que la modale n'est pas ouverte — le getter ne doit pas exploser,
    // Alpine l'évalue au montage du gabarit, avant tout clic.
    view.deleteConfirmation = '2025-2026';
    assert.equal(view.deleteConfirmationMatches, false);
});

test('le régime de l\'établissement est lu au chargement', async () => {
    const test_ = await schoolYearsView({ mode: { isLive: false } });
    assert.equal(test_.isLiveMode, false, 'mode test : la modale annonce un effacement définitif');

    const live = await schoolYearsView({ mode: { isLive: true } });
    assert.equal(live.isLiveMode, true, 'mode réel : la modale annonce un archivage conditionnel');
});

test('mode illisible : on annonce le régime le moins destructeur', async () => {
    const view = await schoolYearsView({ modeFails: true });

    assert.equal(view.isLiveMode, true,
        'promettre un effacement que le serveur refusera ensuite serait pire que trop de prudence');
});

test('la confirmation et l\'erreur sont oubliées à la fermeture de la modale', async () => {
    const view = await schoolYearsView();
    view.openDelete(YEAR);
    view.deleteConfirmation = '2025-2026';
    view.deleteError = 'Erreur précédente';

    view.closeDelete();

    assert.equal(view.yearToDelete, null);
    assert.equal(view.deleteConfirmation, '');
    assert.equal(view.deleteError, null);
});

// ---------------------------------------------------------------------------------------------------------------
// Mode réel : suppression guidée (analyse, blocage financier, rupture explicite avant la suppression)
// ---------------------------------------------------------------------------------------------------------------

/** Composant en mode réel, avec une analyse serveur donnée ; `calls` garde l'ordre des appels d'écriture. */
async function liveView(impact) {
    const calls = [];
    const ctx = loadScripts(['school-years.js'], {
        preload: {
            auth: { role: 'Directeur' },
            api: {
                get: async (endpoint) => {
                    if (endpoint === '/schools/current/mode') return { isLive: true };
                    if (endpoint.endsWith('/deletion-impact')) { calls.push(`GET ${endpoint}`); return impact; }
                    return [YEAR];
                },
                post: async (endpoint) => { calls.push(`POST ${endpoint}`); return impact; },
                delete: async (endpoint) => { calls.push(`DELETE ${endpoint}`); return { label: YEAR.label, wasPurged: false }; },
                toMessage: (_e, fallback) => fallback,
                toFieldErrors: (_e, fallback) => ({ global: fallback })
            },
            softDeleteTrash: { notifyChanged() {} }
        }
    });
    const view = ctx.component('schoolYearsView');
    await flush();
    return { view, calls };
}

const LINKS = {
    canDelete: false, financialBlock: false, message: 'Cette année scolaire est actuellement rattachée à 2 inscriptions.',
    links: [{ kind: 'Enrollment', label: '2 inscriptions', count: 2, breakable: true, breakAction: "Annuler l'inscription et l'archiver" }]
};

test('mode réel : ouvrir la modale lit l\'analyse, mode test : aucune lecture', async () => {
    const live = await liveView(LINKS);
    await live.view.openDelete(YEAR);
    assert.deepEqual(plainCalls(live.calls), ['GET /school-years/y-1/deletion-impact']);
    assert.equal(live.view.deleteImpact.links.length, 1);

    const test_ = await schoolYearsView({ mode: { isLive: false } });
    await test_.openDelete(YEAR);
    assert.equal(test_.deleteImpact, null, "l'effacement définitif de la zone de test n'a rien à analyser");
});

function plainCalls(calls) { return JSON.parse(JSON.stringify(calls)); }

test('mode réel : des liens rompables exigent la case cochée en plus du libellé', async () => {
    const { view } = await liveView(LINKS);
    await view.openDelete(YEAR);
    view.deleteConfirmation = '2025-2026';

    assert.equal(view.deleteNeedsLinksConfirmation, true);
    assert.equal(view.canSubmitDelete, false, 'case non cochée : le bouton reste inerte');

    view.deleteLinksConfirmed = true;
    assert.equal(view.canSubmitDelete, true);
});

test('mode réel : un blocage financier ne se contourne jamais', async () => {
    const { view } = await liveView({ canDelete: false, financialBlock: true, message: 'Impossible de supprimer cette année scolaire…', links: [] });
    await view.openDelete(YEAR);
    view.deleteConfirmation = '2025-2026';
    view.deleteLinksConfirmed = true;

    assert.equal(view.deleteBlocked, true);
    assert.equal(view.canSubmitDelete, false);
});

test('mode réel : un lien non rompable bloque aussi', async () => {
    const { view } = await liveView({ ...LINKS, links: [{ ...LINKS.links[0], breakable: false }] });
    await view.openDelete(YEAR);
    view.deleteConfirmation = '2025-2026';
    view.deleteLinksConfirmed = true;

    assert.equal(view.canSubmitDelete, false);
});

test('mode réel : la rupture précède la suppression, en deux appels distincts', async () => {
    const { view, calls } = await liveView(LINKS);
    await view.openDelete(YEAR);
    view.deleteConfirmation = '2025-2026';
    view.deleteLinksConfirmed = true;

    await view.submitDelete();

    assert.deepEqual(plainCalls(calls), [
        'GET /school-years/y-1/deletion-impact',
        'POST /school-years/y-1/unlink-dependencies',
        'DELETE /school-years/y-1'
    ]);
});

test('mode réel : une année sans lien se supprime sans rupture', async () => {
    const { view, calls } = await liveView({ canDelete: true, financialBlock: false, message: null, links: [] });
    await view.openDelete(YEAR);
    view.deleteConfirmation = '2025-2026';

    assert.equal(view.deleteNeedsLinksConfirmation, false);
    await view.submitDelete();

    assert.deepEqual(plainCalls(calls), ['GET /school-years/y-1/deletion-impact', 'DELETE /school-years/y-1']);
});
