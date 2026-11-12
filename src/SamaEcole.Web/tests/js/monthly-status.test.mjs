/**
 * Situation des mensualités (monthly-status.js) : filtres obligatoires, classe facultative, export figé sur les
 * filtres de la situation affichée, et interrupteur du Directeur conservé par l'écran Paramètres.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

async function boot() {
    const calls = [];
    const api = {
        get: async (endpoint) => {
            calls.push(endpoint);
            if (endpoint === '/school-years') return [{ id: 'y1', label: '2026-2027', isActive: true }];
            if (endpoint === '/classrooms') return [{ id: 'c1', name: 'CM2', level: 'Primaire' }];
            if (endpoint.startsWith('/finance/monthly-status')) {
                return { month: 10, summary: { totalStudents: 0 }, paid: [], partial: [], unpaid: [] };
            }
            return [];
        },
        toMessage: (e, fallback) => (e && e.message) || fallback
    };
    const ctx = loadScripts(['monthly-status.js'], { preload: { api, formatFCFA: (n) => String(n) } });
    const view = ctx.component('monthlyStatusView');
    await flush();
    return { view, calls };
}

test("l'année active est présélectionnée et sa situation du mois courant se charge dès l'arrivée", async () => {
    const { view, calls } = await boot();
    assert.equal(view.schoolYearId, 'y1');
    assert.equal(calls.some((c) => c.startsWith('/finance/monthly-status?') && c.includes('schoolYearId=y1')), true);
});

test('sans année ou sans mois, rien n\'est demandé et un message est affiché', async () => {
    const { view, calls } = await boot();
    const isStatusCall = (c) => c.startsWith('/finance/monthly-status');
    const before = calls.filter(isStatusCall).length; // chargement initial (année active, mois courant)
    view.month = '';
    await view.load();
    assert.ok(view.formError);
    assert.equal(calls.filter(isStatusCall).length, before, 'aucune requête de plus sans mois');
});

test("sans classe choisie, aucun classroomId n'est envoyé ; avec une classe, il l'est", async () => {
    const { view, calls } = await boot();
    view.month = '10';
    await view.load();
    const first = calls.filter((c) => c.startsWith('/finance/monthly-status')).pop();
    assert.ok(first.includes('schoolYearId=y1') && first.includes('month=10'));
    assert.equal(first.includes('classroomId'), false);

    view.classroomId = 'c1';
    await view.load();
    const last = calls.filter((c) => c.startsWith('/finance/monthly-status')).pop();
    assert.ok(last.includes('classroomId=c1'));
});

test("l'export reprend les filtres de la situation affichée, pas ceux modifiés depuis", async () => {
    const { view } = await boot();
    view.month = '10';
    await view.load();
    view.month = '3'; // modifié sans recliquer sur « Afficher »
    const params = view.buildParams(view.applied).toString();
    assert.ok(params.includes('month=10'));
});
