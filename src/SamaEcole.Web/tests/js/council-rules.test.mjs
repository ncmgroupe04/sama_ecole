/**
 * Seuils du conseil de classe (`councilRulesPanel`, wwwroot/js/council-rules.js) — Évolution N°7.
 * Le panneau ne calcule rien : il lit, envoie des nombres (virgule française acceptée), et n'offre l'écriture
 * qu'au Directeur.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const RULES = { felicitationsMin: 15, honorRollMin: 13, encouragementsMin: 12, eliminatoryGrade: 5, promotionMin: 10, repeatMin: 8.5 };

function panel(role = 'Directeur') {
    const calls = [];
    const ctx = loadScripts(['council-rules.js'], {
        preload: {
            auth: { role },
            api: {
                get: async (endpoint) => { calls.push({ method: 'GET', endpoint }); return RULES; },
                put: async (endpoint, body) => { calls.push({ method: 'PUT', endpoint, body: plain(body) }); return plain(body); },
                toMessage: (_e, fallback) => fallback
            }
        }
    });
    return { view: ctx.component('councilRulesPanel'), calls };
}

test('les seuils de l\'école sont chargés et affichés à la française', async () => {
    const { view } = panel();
    await flush();

    assert.equal(view.form.felicitationsMin, '15');
    assert.equal(view.form.repeatMin, '8,5');
});

test('l\'enregistrement envoie des nombres, virgule comprise', async () => {
    const { view, calls } = panel();
    await flush();

    view.form.repeatMin = '7,5';
    await view.save();

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/report-cards/council-rules');
    assert.equal(put.body.repeatMin, 7.5);
    assert.equal(put.body.felicitationsMin, 15);
});

test('un seuil illisible n\'est jamais envoyé', async () => {
    const { view, calls } = panel();
    await flush();

    view.form.promotionMin = 'dix';
    await view.save();

    assert.equal(calls.some((c) => c.method === 'PUT'), false);
    assert.match(view.error, /nombre/);
});

test('« Revenir aux valeurs du Ministère » restaure 14 / 12 / 12 / 5 / 10 / 8,5', async () => {
    const { view } = panel();
    await flush();

    view.resetDefaults();
    assert.deepEqual(plain(view.form), {
        felicitationsMin: '14', honorRollMin: '12', encouragementsMin: '12',
        eliminatoryGrade: '5', promotionMin: '10', repeatMin: '8,5'
    });
});

test('seul le Directeur peut modifier', async () => {
    assert.equal(panel('Secretariat').view.canEdit, false);
    assert.equal(panel('Directeur').view.canEdit, true);
});
