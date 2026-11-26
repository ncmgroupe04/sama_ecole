/**
 * Écran Paie — le sélecteur d'enseignants passe par la route DÉDIÉE à la Paie, pas par GET /teachers.
 *
 * Recette du 15/11/2026 : GET /teachers est fermé à la Finance (données personnelles). L'écran l'appelait quand même :
 * bandeau « Vous n'avez pas les droits nécessaires » à chaque ouverture et sélecteur vide, donc aucun contrat
 * d'enseignant possible pour la Finance.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush } from './harness.mjs';

function boot({ role, teachersFail = false }) {
    const gets = [];
    const toasts = [];
    const api = {
        get: async (url) => {
            gets.push(url);
            if (url === '/finance/employee-contracts/teacher-options') {
                if (teachersFail) throw Object.assign(new Error('boom'), { status: 500 });
                return [{ id: 't1', matricule: 'ENS-001', fullName: 'Awa Fall' }];
            }
            return [];
        },
        toMessage: (err, fallback) => fallback
    };
    const ctx = loadScripts(['payroll.js'], {
        preload: { auth: { role, schoolId: 's1' }, api, pdfPreview: { state: () => ({}) } }
    });
    ctx.sandbox.api = api;
    ctx.sandbox.toast = { success() {}, error: (m) => toasts.push(m) };
    const view = ctx.component('payrollView');
    return { view, gets, toasts };
}

test('le sélecteur lit la route dédiée à la Paie, jamais GET /teachers', async () => {
    const { view, gets } = boot({ role: 'Finance' });

    await view.loadTeachers();

    assert.ok(gets.includes('/finance/employee-contracts/teacher-options'));
    assert.ok(!gets.some(u => u.startsWith('/teachers')), 'GET /teachers est fermé à la Finance');
    assert.equal(view.teachers.length, 1);
    assert.equal(view.teachers[0].fullName, 'Awa Fall');
});

test('un vrai échec reste signalé et laisse un sélecteur vide', async () => {
    const { view, toasts } = boot({ role: 'Directeur', teachersFail: true });

    await view.loadTeachers();
    await flush();

    assert.equal(view.teachers.length, 0);
    assert.ok(toasts.length >= 1, "l'échec est signalé à l'utilisateur");
});
