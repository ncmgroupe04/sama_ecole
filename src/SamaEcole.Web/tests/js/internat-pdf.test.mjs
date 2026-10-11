/**
 * Impression PDF de l'écran Internat (lot G4, tâche 9) : fiche d'un pensionnaire et registre d'un pavillon.
 *
 * Les routes PDF exigent l'en-tête Authorization : un simple lien <a href> ne peut pas les ouvrir. L'écran passe donc par la modale commune
 * `window.pdfPreview` (pdf-preview.js, déjà utilisée par le bulletin coranique, les reçus, etc.) qui récupère le document avec le jeton, l'affiche,
 * et montre le message de l'API sur 403/404/422 au lieu d'un onglet vide. Ces tests prouvent l'URL, la date, et que l'écran délègue.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

const ID = '3f2c1a40-0000-4000-8000-000000000001';

function helpers() {
    return loadScripts(['internat-helpers.js']).window.internatHelpers;
}

test('boarderPdfUrl : route de la fiche individuelle, sans paramètre', () => {
    const h = helpers();
    assert.equal(h.boarderPdfUrl(ID), `/api/v1/boarding/boarders/${ID}/pdf`);
    assert.equal(h.boarderPdfUrl(''), '', "sans identifiant, aucune URL n'est fabriquée");
});

test('registerPdfUrl : le registre envoie ?date= au format yyyy-MM-dd', () => {
    const h = helpers();
    assert.equal(h.registerPdfUrl(ID, '2026-10-07'), `/api/v1/boarding/dormitories/${ID}/register-pdf?date=2026-10-07`);
});

test('registerPdfUrl : un objet Date est formaté avec ses composantes LOCALES (jamais un décalage UTC)', () => {
    const h = helpers();
    // 1er janvier 00:30 local : toISOString() donnerait la veille dans un fuseau positif.
    assert.equal(h.registerPdfUrl(ID, new Date(2026, 0, 1, 0, 30)), `/api/v1/boarding/dormitories/${ID}/register-pdf?date=2026-01-01`);
    assert.equal(h.registerPdfUrl(ID, new Date(2026, 11, 31, 23, 59)), `/api/v1/boarding/dormitories/${ID}/register-pdf?date=2026-12-31`);
});

test("registerPdfUrl : sans date, le serveur prend aujourd'hui ; une date mal formée n'est pas envoyée", () => {
    const h = helpers();
    assert.equal(h.registerPdfUrl(ID), `/api/v1/boarding/dormitories/${ID}/register-pdf`);
    assert.equal(h.registerPdfUrl(ID, ''), `/api/v1/boarding/dormitories/${ID}/register-pdf`);
    assert.equal(h.registerPdfUrl(ID, '07/10/2026'), `/api/v1/boarding/dormitories/${ID}/register-pdf`);
    assert.equal(h.registerPdfUrl(ID, 'n\'importe quoi'), `/api/v1/boarding/dormitories/${ID}/register-pdf`);
    assert.equal(h.registerPdfUrl('', '2026-10-07'), '');
});

test('pdfFileName : nom ASCII, sans caractère dangereux', () => {
    const h = helpers();
    assert.equal(h.pdfFileName('Fiche-Pensionnaire', 'M-2026/001'), 'Fiche-Pensionnaire-M-2026001.pdf');
    assert.equal(h.pdfFileName('Registre-Pavillon', 'Pavillon Oustaz Ahmad', '2026-10-07'), 'Registre-Pavillon-Pavillon-Oustaz-Ahmad-2026-10-07.pdf');
    assert.equal(h.pdfFileName('Fiche-Pensionnaire', '../../etc'), 'Fiche-Pensionnaire-etc.pdf');
    assert.equal(h.pdfFileName('Fiche-Pensionnaire', 'Élève Ça'), 'Fiche-Pensionnaire-Eleve-Ca.pdf', 'les accents sont ramenés à leur lettre de base');
});

// ---------------------------------------------------------------- l'onglet délègue à la modale commune

function bootBoarders(role) {
    const previews = [];
    const ctx = loadScripts(['internat-helpers.js', 'internat-boarders.js'], {
        preload: {
            auth: { role },
            pdfPreview: {
                state: () => ({ showPdfModal: false, openPdfPreview: async (...args) => { previews.push(args); } })
            },
            api: {
                get: async (endpoint) => (endpoint.startsWith('/boarding/boarders?') ? { items: [], totalCount: 0 } : []),
                toMessage: (err, fallback) => (err && err.message) || fallback
            }
        }
    });
    return { ctx, tab: ctx.component('boardersTab'), previews };
}

const DETAIL = { boarder: { id: ID, matricule: 'M-001', studentName: 'Awa Diop' } };

test("« Fiche PDF » ouvre la modale commune sur la route du séjour, pour les trois rôles", async () => {
    for (const role of ['Directeur', 'Surveillant', 'Secretariat']) {
        const { tab, previews } = bootBoarders(role);
        await flush();
        tab.panel.data = DETAIL;
        await tab.openBoarderPdf();

        assert.equal(previews.length, 1, role);
        const [url, title, fileName] = previews[0];
        assert.equal(url, `/api/v1/boarding/boarders/${ID}/pdf`, role);
        assert.match(title, /Awa Diop/, role);
        assert.equal(fileName, 'Fiche-Pensionnaire-M-001.pdf', role);
    }
});

test("la fiche PDF n'envoie aucun paramètre de rôle ou de médical : le masquage est décidé par le serveur", async () => {
    const { tab, previews } = bootBoarders('Secretariat');
    await flush();
    tab.panel.data = DETAIL;
    await tab.openBoarderPdf();

    assert.doesNotMatch(previews[0][0], /[?&]/, "aucune chaîne de requête : l'écran ne choisit jamais ce que le document contient");
});

test('sans fiche ouverte, aucune impression', async () => {
    const { tab, previews } = bootBoarders('Directeur');
    await flush();
    tab.panel.data = null;
    await tab.openBoarderPdf();
    assert.equal(previews.length, 0);
});

test("le composant étale l'état de la modale PDF commune (pas de copie du mécanisme)", async () => {
    const { tab } = bootBoarders('Directeur');
    assert.equal(tab.showPdfModal, false);
    assert.equal(typeof tab.openPdfPreview, 'function');
});

// ---------------------------------------------------------------- défaut trouvé à la recette navigateur

test("« Fiche PDF » appelle le moteur sur le composant RACINE, pas sur l'élément cliqué (qui disparaît quand le moteur ferme la fiche)", async () => {
    // Le moteur d'aperçu ferme d'abord TOUTES les modales : la fiche, donc le bouton cliqué, est retirée du DOM. Alpine résout `this.$refs` par rapport à
    // l'élément qui a déclenché l'appel : devenu orphelin, il ne trouve plus le conteneur des pages et le rendu abandonne en silence (« Génération… »
    // pour toujours). Les tests qui appellent la méthode directement ne pouvaient pas le voir ; seul un vrai clic le montre.
    const ROOT = { id: 'racine' };
    const onRoot = [];
    const rootData = { openPdfPreview: async (...args) => { onRoot.push(args); } };
    const { ctx, tab, previews } = bootBoarders('Directeur');
    ctx.sandbox.Alpine.$data = (element) => (element === ROOT ? rootData : undefined);
    tab.$root = ROOT;
    tab.panel.data = DETAIL;

    await tab.openBoarderPdf();

    assert.equal(onRoot.length, 1, "le moteur doit être appelé sur les données liées à la racine");
    assert.equal(previews.length, 0, "jamais sur `this`, lié à l'élément cliqué");
    assert.equal(onRoot[0][0], `/api/v1/boarding/boarders/${ID}/pdf`);
});

test("sans Alpine ni racine (tests, rendu serveur), « Fiche PDF » retombe sur le composant lui-même", async () => {
    const { tab, previews } = bootBoarders('Directeur');
    tab.panel.data = DETAIL;

    await tab.openBoarderPdf();

    assert.equal(previews.length, 1);
});
