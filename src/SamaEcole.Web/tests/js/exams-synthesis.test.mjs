/**
 * Écran des examens (`examsView`) — Franco-Arabe, bloc 4 : fiche synthétique d'un candidat et PV de session.
 * L'écran ne décide de rien (bilinguisme, filtre des dossiers : tout est côté serveur) : il pointe sur la bonne
 * route et nomme le fichier. On verrouille ici les URL et les noms de fichier.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

function examsView(role = 'Directeur') {
    const ctx = loadScripts(['pdf-preview.js', 'exams.js'], {
        preload: {
            auth: { role },
            api: { get: async () => [], toMessage: (_e, fallback) => fallback, toFieldErrors: (_e, fallback) => ({ global: fallback }) }
        }
    });
    return ctx.component('examsView');
}

test('la fiche synthétique ouvre la route du dossier, nommée d\'après son numéro de table', async () => {
    const view = examsView();
    const calls = [];
    view.openPdfPreview = async (url, title, fileName) => { calls.push({ url, title, fileName }); };

    await view.printCandidateSynthesis({ id: 'd1', studentFullName: 'Awa Fall', candidateNumber: 'BFEM-001' });

    assert.deepEqual(calls, [{
        url: '/api/v1/exams/dossiers/d1/synthesis/pdf',
        title: 'Fiche synthétique — Awa Fall',
        fileName: 'Fiche-Synthetique-BFEM-001.pdf'
    }]);
});

test('sans numéro de table, la fiche est nommée d\'après le candidat', async () => {
    const view = examsView();
    const calls = [];
    view.openPdfPreview = async (url, title, fileName) => { calls.push({ url, title, fileName }); };

    await view.printCandidateSynthesis({ id: 'd2', studentFullName: 'Moussa Ba', candidateNumber: null });

    assert.equal(calls[0].fileName, 'Fiche-Synthetique-Moussa Ba.pdf');
});

test('le PV de session ouvre la route de la session', async () => {
    const view = examsView();
    const calls = [];
    view.openPdfPreview = async (url, title, fileName) => { calls.push({ url, title, fileName }); };

    await view.printSessionPv({ id: 's1', examType: 'BFEM', series: null });

    assert.deepEqual(calls, [{
        url: '/api/v1/exams/sessions/s1/pv/pdf',
        title: 'PV de session — BFEM',
        fileName: 'PV-BFEM.pdf'
    }]);
});

test('le PV d\'un baccalauréat porte sa série dans le titre et le nom de fichier', async () => {
    const view = examsView();
    const calls = [];
    view.openPdfPreview = async (url, title, fileName) => { calls.push({ url, title, fileName }); };

    await view.printSessionPv({ id: 's2', examType: 'BAC', series: 'L' });

    assert.equal(calls[0].title, 'PV de session — BAC L');
    assert.equal(calls[0].fileName, 'PV-BAC-L.pdf');
});

test('un refus du serveur (aucun dossier éligible) remonte à l\'aperçu sans casser l\'écran', async () => {
    const view = examsView();
    view.openPdfPreview = async () => { throw new Error('422'); };

    await assert.rejects(() => view.printSessionPv({ id: 's3', examType: 'CFEE', series: null }), /422/);
});
