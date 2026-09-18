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
