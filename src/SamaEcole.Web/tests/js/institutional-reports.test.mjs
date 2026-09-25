/**
 * Rapports institutionnels (`institutionalReportsView`, wwwroot/js/institutional-reports.js) — Évolution N°7.
 * L'écran n'agrège rien : il charge le rapport de l'année active, envoie la date de référence choisie, et n'offre la
 * modification des normes d'âge qu'au Directeur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const YEARS = [{ id: 'y-old', label: '2025-2026', isActive: false }, { id: 'y-now', label: '2026-2027', isActive: true }];
const NORMS = [{ gradeLevel: 'CI', cycle: 'Primaire', templateMinAge: 5, templateMaxAge: 8, minAge: 5, maxAge: 8, isCustom: false }];

function boot(role = 'Directeur') {
    const calls = [];
    const ctx = loadScripts(['institutional-reports.js'], {
        preload: {
            auth: { role },
            pdfPreview: { state: () => ({ openPdfPreview: async () => {} }) },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/school-years') return YEARS;
                    if (endpoint === '/institutional/age-norms') return NORMS;
                    if (endpoint.startsWith('/institutional/ief-report')) return { classes: [], totalStudents: 0 };
                    return [];
                },
                put: async (endpoint, body) => { calls.push({ method: 'PUT', endpoint, body: plain(body) }); return null; },
                delete: async (endpoint) => { calls.push({ method: 'DELETE', endpoint }); return null; },
                toMessage: (_e, fallback) => fallback
            }
        }
    });
    return { view: ctx.component('institutionalReportsView'), calls };
}

test('le rapport de l\'année ACTIVE est chargé à l\'ouverture', async () => {
    const { view, calls } = boot();
    await flush();

    assert.equal(view.schoolYearId, 'y-now');
    assert.ok(calls.some((c) => c.endpoint === '/institutional/ief-report?schoolYearId=y-now'));
});

test('la date de référence des âges est transmise quand elle est choisie', async () => {
    const { view, calls } = boot();
    await flush();

    view.ageReferenceDate = '2026-10-01';
    await view.load();
    assert.ok(calls.some((c) => c.endpoint === '/institutional/ief-report?schoolYearId=y-now&ageReferenceDate=2026-10-01'));
});

test('une tranche d\'âge s\'enregistre en âges entiers', async () => {
    const { view, calls } = boot();
    await flush();

    const ci = view.norms[0];
    ci.draftMax = '9';
    assert.equal(view.normDirty(ci), true);
    await view.saveNorm(ci);

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/institutional/age-norms/CI');
    assert.deepEqual(put.body, { minAge: 5, maxAge: 9 });
});

test('le Secrétariat consulte les normes sans pouvoir les modifier', () => {
    assert.equal(boot('Secretariat').view.canEditNorms, false);
    assert.equal(boot('Directeur').view.canEditNorms, true);
});
