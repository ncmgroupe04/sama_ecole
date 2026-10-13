/**
 * Espace de l'Oustaz (/halqa) : logique pure (`window.halqaLogic`) et composant `halqaPage` (wwwroot/js/halqa.js).
 * Le serveur décide de la portée, de l'état d'un Hizb et de la date d'évaluation ; ces tests prouvent que l'écran
 * n'envoie que ce qu'il doit (jamais d'état ni de date), recharge sur conflit au lieu d'écraser, et affiche le nom
 * arabe avec repli sur le nom français.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

function cell(hizbNumber, completedQuarters = 0, extra = {}) {
    const state = completedQuarters === 0 ? 'NotStarted' : completedQuarters >= 4 ? 'Completed' : 'InProgress';
    return { hizbNumber, completedQuarters, state, lastEvaluatedAt: null, rating: null, rowVersion: null, ...extra };
}

/** Une grille de 60 cases ; `overrides` remplace des cases par numéro de Hizb. */
function gridOf(overrides = {}) {
    const hizbs = Array.from({ length: 60 }, (_, i) => overrides[i + 1] ?? cell(i + 1));
    return {
        studentId: 's1', fullName: 'Awa Diop', instructorId: 'i1',
        summary: { completedHizbs: 0, inProgressHizbs: 0, completedQuarters: 0, totalQuarters: 240, progressPercent: 0 },
        hizbs
    };
}

/** Réponse de GET hizb-history : niveau d'ouverture de 4 quarts, deux jours de saisie, deux dernières évaluations. */
const HISTORY = {
    studentId: 's1', fullName: 'Awa Diop', from: '2026-04-15', to: '2026-10-15',
    currentTotalQuarters: 9, currentProgressPercent: 3.8, openingTotalQuarters: 4, firstEvaluationAt: '2026-03-01T10:00:00Z',
    points: [
        { date: '2026-08-10', totalQuarters: 6, progressPercent: 2.5 },
        { date: '2026-09-20', totalQuarters: 9, progressPercent: 3.8 }
    ],
    recent: [
        { evaluatedAt: '2026-09-20T09:00:00Z', hizbNumber: 2, quartersBefore: 1, quartersAfter: 3, rating: 4, totalQuartersAfter: 9, isBaseline: false, evaluatedBy: 'Ahmad Lô' },
        { evaluatedAt: '2026-03-01T10:00:00Z', hizbNumber: 1, quartersBefore: 0, quartersAfter: 4, rating: null, totalQuartersAfter: 4, isBaseline: true, evaluatedBy: null }
    ]
};

const HALQA = {
    instructorId: 'i1', instructorName: 'Serigne Modou', instructorNameAr: 'سيرين مودو',
    studentCount: 2, averageProgressPercent: 1.3,
    students: [
        { studentId: 's1', matricule: 'ELEV-1', fullName: 'Awa Diop', fullNameAr: 'عائشة جوب', completedHizbs: 1, inProgressHizbs: 0, completedQuarters: 4, progressPercent: 1.7, lastEvaluatedAt: null },
        { studentId: 's2', matricule: 'ELEV-2', fullName: 'Binta Fall', fullNameAr: null, completedHizbs: 0, inProgressHizbs: 0, completedQuarters: 0, progressPercent: 0, lastEvaluatedAt: null }
    ]
};

/**
 * @param {object} options
 * @param {object} options.grid     réponse de GET hizb-progress
 * @param {Function} options.put    comportement de PUT (reçoit endpoint, body) ; renvoie la case enregistrée ou lève
 * @param {Error} options.halqaError erreur levée par GET my-halqa
 */
function boot(options = {}) {
    const calls = [];
    const ctx = loadScripts(['trend-chart.js', 'halqa.js'], {
        preload: {
            pdfPreview: {
                // Doublure de la modale PDF commune : on ne teste pas pdf-preview.js ici, seulement l'appel qu'on lui adresse.
                state: () => ({
                    openPdfPreview: async (url, title, downloadName) => { calls.push({ method: 'PDF', url, title, downloadName }); }
                })
            },
            api: {
                get: async (endpoint) => {
                    calls.push({ method: 'GET', endpoint });
                    if (endpoint === '/internat/my-halqa') {
                        if (options.halqaError) throw options.halqaError;
                        return HALQA;
                    }
                    if (endpoint.endsWith('/hizb-progress')) return options.grid ?? gridOf();
                    if (endpoint.includes('/hizb-history')) {
                        if (options.historyError) throw options.historyError;
                        return options.history ?? HISTORY;
                    }
                    return null;
                },
                put: async (endpoint, body) => {
                    calls.push({ method: 'PUT', endpoint, body: plain(body) });
                    if (options.put) return options.put(endpoint, body);
                    return cell(body.hizbNumber, body.completedQuarters, { rating: body.rating, rowVersion: 99 });
                },
                toMessage: (err, fallback) => (err && err.handledGlobally ? null : fallback)
            }
        }
    });
    const components = ctx.initAlpine();
    return { page: components.get('halqaPage')(), logic: ctx.window.halqaLogic, calls };
}

function httpError(status, extra = {}) {
    return Object.assign(new Error('HTTP ' + status), { status }, extra);
}

// ---------------------------------------------------------------------------------------------- logique pure

test('le nom arabe est affiché, avec repli sur le nom français s\'il est absent ou blanc', () => {
    const { logic } = boot();
    assert.equal(logic.pickName('عائشة جوب', 'Awa Diop'), 'عائشة جوب');
    assert.equal(logic.pickName('  عائشة  ', 'Awa Diop'), 'عائشة', 'l\'arabe est rogné');
    assert.equal(logic.pickName(null, 'Awa Diop'), 'Awa Diop');
    assert.equal(logic.pickName(undefined, 'Awa Diop'), 'Awa Diop');
    assert.equal(logic.pickName('', 'Awa Diop'), 'Awa Diop');
    assert.equal(logic.pickName('   ', 'Awa Diop'), 'Awa Diop', 'un arabe fait d\'espaces retombe sur le français');
    assert.equal(logic.pickName(null, null), '', 'jamais « undefined » à l\'écran');
});

test('chaque Juz contient deux Hizb consécutifs : 1-2, 3-4, … 59-60', () => {
    const { logic } = boot();
    assert.equal(logic.juzOfHizb(1), 1);
    assert.equal(logic.juzOfHizb(2), 1);
    assert.equal(logic.juzOfHizb(3), 2);
    assert.equal(logic.juzOfHizb(59), 30);
    assert.equal(logic.juzOfHizb(60), 30);
});

test('la grille de 60 Hizb se regroupe en 30 Juz de deux Hizb, dans l\'ordre', () => {
    const { logic } = boot();
    const groups = plain(logic.groupByJuz(gridOf().hizbs));

    assert.equal(groups.length, 30);
    assert.ok(groups.every((g) => g.hizbs.length === 2));
    assert.deepEqual(groups.map((g) => g.juz), Array.from({ length: 30 }, (_, i) => i + 1));
    assert.deepEqual(groups[0].hizbs.map((c) => c.hizbNumber), [1, 2]);
    assert.deepEqual(groups[29].hizbs.map((c) => c.hizbNumber), [59, 60]);
    assert.deepEqual(plain(logic.groupByJuz(null)), [], 'une grille absente ne casse pas l\'écran');
});

test('les libellés des quarts sont ceux de l\'Oustaz, en arabe', () => {
    const { logic } = boot();
    assert.equal(logic.quarterLabel(1), 'الربع الأول');
    assert.equal(logic.quarterLabel(2), 'النصف');
    assert.equal(logic.quarterLabel(3), 'ثلاثة أرباع');
    assert.equal(logic.quarterLabel(4), 'الحزب كامل');
    assert.equal(logic.quarterLabel(0), 'لم يبدأ');
    assert.equal(logic.quarterLabel(9), '', 'une valeur hors domaine n\'affiche rien');
});

test('le résumé se calcule comme le serveur : quarts sur 240, une décimale, arrondi loin de zéro', () => {
    const { logic } = boot();
    const s = plain(logic.summarize([cell(1, 4), cell(2, 2)]));
    assert.deepEqual(s, { completedHizbs: 1, inProgressHizbs: 1, completedQuarters: 6, totalQuarters: 240, progressPercent: 2.5 });

    assert.equal(logic.summarize([cell(1, 3)]).progressPercent, 1.3, '3/240 = 1,25 → 1,3 (loin de zéro, comme HizbRules)');
    assert.equal(logic.summarize([cell(1, 1)]).progressPercent, 0.4);
    assert.equal(logic.summarize([]).progressPercent, 0);
    assert.equal(logic.summarize(Array.from({ length: 60 }, (_, i) => cell(i + 1, 4))).progressPercent, 100);
});

test('une case change de teinte selon son état et dessine ses quarts', () => {
    const { logic } = boot();
    assert.match(logic.cellClasses('Completed'), /emerald/);
    assert.match(logic.cellClasses('InProgress'), /amber/);
    assert.match(logic.cellClasses('NotStarted'), /slate/);
    assert.match(logic.cellClasses('autre'), /slate/, 'état inconnu : traité comme non commencé');

    const c = cell(5, 2);
    assert.deepEqual([1, 2, 3, 4].map((q) => logic.quarterFilled(c, q)), [true, true, false, false]);
});

// ---------------------------------------------------------------------------------------------- chargement

test('l\'ouverture charge la Halqa de l\'Oustaz connecté, sans aucun identifiant', async () => {
    const { page, calls } = boot();
    await page.init();

    assert.deepEqual(calls, [{ method: 'GET', endpoint: '/internat/my-halqa' }]);
    assert.equal(page.halqa.students.length, 2);
    assert.equal(page.loading, false);
    assert.equal(page.noHalqa, false);
    assert.equal(page.error, null);
});

test('un compte sans fiche d\'Oustaz (403) voit un état vide, pas une panne', async () => {
    const { page } = boot({ halqaError: httpError(403, { handledGlobally: true }) });
    await page.init();

    assert.equal(page.noHalqa, true);
    assert.equal(page.error, null);
    assert.equal(page.halqa, null);
    assert.equal(page.loading, false);
});

test('toute autre erreur de chargement est affichée', async () => {
    const { page } = boot({ halqaError: httpError(500) });
    await page.init();

    assert.equal(page.noHalqa, false);
    assert.equal(page.error, page.labels.loadError);
    assert.equal(page.loading, false);
});

test('ouvrir un élève charge sa grille et la regroupe en 30 Juz', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    assert.ok(calls.some((c) => c.endpoint === '/internat/students/s1/hizb-progress'));
    assert.equal(page.groups.length, 30);
    assert.equal(page.selected.studentId, 's1');
    assert.equal(page.gridLoading, false);
});

test('revenir à la liste relit la Halqa : les indicateurs ont pu changer pendant la saisie', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    await page.closeStudent();

    assert.equal(page.selected, null);
    assert.deepEqual(plain(page.groups), []);
    assert.equal(calls.filter((c) => c.endpoint === '/internat/my-halqa').length, 2);
});

// ---------------------------------------------------------------------------------------------- tiroir

test('le tiroir s\'ouvre sur la case choisie, préremplie, et n\'autorise à enregistrer qu\'un changement', async () => {
    const { page } = boot({ grid: gridOf({ 7: cell(7, 2, { rating: 4, rowVersion: 31 }) }) });
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    page.openDrawer(page.grid.hizbs[6]);
    assert.equal(page.drawer.open, true);
    assert.equal(page.drawer.juz, 4);
    assert.equal(page.drawer.quarters, 2);
    assert.equal(page.drawer.rating, 4);
    assert.equal(page.canSave, false, 'rien n\'a changé');

    page.pickQuarters(3);
    assert.equal(page.canSave, true);
    page.pickQuarters(2);
    assert.equal(page.canSave, false, 'revenu à la valeur d\'origine');
});

test('un Hizb non commencé n\'a pas de note : choisir « لم يبدأ » la retire, et la note est alors refusée', async () => {
    const { page } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(cell(7, 2, { rating: 4, rowVersion: 31 }));

    page.pickQuarters(0);
    assert.equal(page.drawer.rating, null);
    page.pickRating(5);
    assert.equal(page.drawer.rating, null, 'impossible de noter un Hizb non commencé');
});

test('retoucher la note choisie l\'annule : elle est facultative', async () => {
    const { page } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(cell(7, 0));

    page.pickQuarters(2);
    page.pickRating(4);
    assert.equal(page.drawer.rating, 4);
    page.pickRating(4);
    assert.equal(page.drawer.rating, null);
});

// ---------------------------------------------------------------------------------------------- enregistrement

test('un Hizb jamais saisi s\'enregistre sans jeton, et sans état ni date : le serveur les décide', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[4]);

    page.pickQuarters(2);
    page.pickRating(3);
    await page.save();

    const put = calls.find((c) => c.method === 'PUT');
    assert.equal(put.endpoint, '/internat/students/s1/hizb-progress');
    assert.deepEqual(put.body, { hizbNumber: 5, completedQuarters: 2, rating: 3, rowVersion: null });
    assert.ok(!('state' in put.body) && !('lastEvaluatedAt' in put.body), 'aucun état ni date envoyés');
});

test('un Hizb déjà saisi renvoie son jeton xmin', async () => {
    const { page, calls } = boot({ grid: gridOf({ 7: cell(7, 1, { rating: 2, rowVersion: 31 }) }) });
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[6]);

    page.pickQuarters(4);
    await page.save();

    assert.equal(calls.find((c) => c.method === 'PUT').body.rowVersion, 31);
});

test('mettre un Hizb à zéro quart n\'envoie jamais de note', async () => {
    const { page, calls } = boot({ grid: gridOf({ 7: cell(7, 3, { rating: 5, rowVersion: 31 }) }) });
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[6]);

    page.pickQuarters(0);
    await page.save();

    assert.deepEqual(calls.find((c) => c.method === 'PUT').body, { hizbNumber: 7, completedQuarters: 0, rating: null, rowVersion: 31 });
});

test('après l\'enregistrement, la case est remplacée par celle du serveur et l\'en-tête recalculé', async () => {
    const { page } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[0]);

    page.pickQuarters(4);
    page.pickRating(5);
    await page.save();

    assert.equal(page.drawer.open, false, 'le tiroir se ferme');
    const saved = page.grid.hizbs[0];
    assert.equal(saved.completedQuarters, 4);
    assert.equal(saved.rowVersion, 99, 'le nouveau jeton du serveur remplace l\'ancien');
    assert.equal(page.grid.hizbs.length, 60);
    assert.equal(page.grid.summary.completedHizbs, 1);
    assert.equal(page.grid.summary.progressPercent, 1.7);
    assert.equal(page.groups[0].hizbs[0].completedQuarters, 4, 'la grille regroupée est à jour');
});

test('un conflit (409) recharge la grille au lieu d\'écraser, et le dit', async () => {
    const { page, calls } = boot({ put: async () => { throw httpError(409); } });
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[2]);
    page.pickQuarters(3);

    const gridReadsBefore = calls.filter((c) => c.method === 'GET' && c.endpoint.endsWith('/hizb-progress')).length;
    await page.save();

    assert.equal(page.drawer.open, false);
    assert.equal(page.notice, page.labels.conflict);
    assert.equal(calls.filter((c) => c.method === 'GET' && c.endpoint.endsWith('/hizb-progress')).length, gridReadsBefore + 1, 'la grille est relue');
    assert.equal(page.grid.hizbs[2].completedQuarters, 0, 'rien n\'a été écrasé localement');
    assert.equal(page.error, null, 'un conflit n\'est pas une erreur bloquante');
});

test('une autre erreur d\'enregistrement garde le tiroir ouvert avec le message, sans recharger', async () => {
    const { page, calls } = boot({ put: async () => { throw httpError(422); } });
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[2]);
    page.pickQuarters(3);

    const gridReadsBefore = calls.filter((c) => c.method === 'GET' && c.endpoint.endsWith('/hizb-progress')).length;
    await page.save();

    assert.equal(page.drawer.open, true, 'l\'Oustaz garde sa saisie');
    assert.equal(page.drawer.error, page.labels.saveError);
    assert.equal(page.drawer.saving, false, 'le bouton redevient actif pour réessayer');
    assert.equal(calls.filter((c) => c.method === 'GET' && c.endpoint.endsWith('/hizb-progress')).length, gridReadsBefore);
});

test('enregistrer sans rien changer n\'envoie rien', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[0]);

    await page.save();

    assert.equal(calls.filter((c) => c.method === 'PUT').length, 0);
});

// ------------------------------------------------------------------------------------------- bulletin coranique PDF

test('« كشف الحفظ » ouvre le bulletin PDF de l\'élève ouvert, sans attendre sa grille', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    await page.openReport();

    assert.deepEqual(calls.find((c) => c.method === 'PDF'), {
        method: 'PDF',
        url: '/api/v1/internat/students/s1/hizb-report/pdf',
        title: 'Bulletin coranique — Awa Diop',
        downloadName: 'Bulletin-Coranique-ELEV-1.pdf'
    });
});

test('sans élève ouvert, aucun bulletin n\'est demandé', async () => {
    const { page, calls } = boot();
    await page.init();

    await page.openReport();

    assert.equal(calls.filter((c) => c.method === 'PDF').length, 0);
});

test('le nom du fichier du bulletin ne garde que des caractères sûrs', () => {
    const { logic } = boot();
    assert.equal(logic.reportUrl('abc'), '/api/v1/internat/students/abc/hizb-report/pdf');
    assert.equal(logic.reportFileName('ELEV-2026/0001 é'), 'Bulletin-Coranique-ELEV-20260001.pdf');
    assert.equal(logic.reportFileName(null), 'Bulletin-Coranique-.pdf');
});

// ------------------------------------------------------------------------------------- évolution (journal des évaluations)

const historyCalls = (calls) => calls.filter((c) => c.method === 'GET' && c.endpoint.includes('/hizb-history'));

test('ouvrir un élève charge son évolution sur les six derniers mois', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    const reads = historyCalls(calls);
    assert.equal(reads.length, 1);
    assert.match(reads[0].endpoint, /^\/internat\/students\/s1\/hizb-history\?from=\d{4}-\d{2}-\d{2}&to=\d{4}-\d{2}-\d{2}$/);
    assert.equal(page.historyPeriod, '6m');
    assert.equal(page.history.points.length, 2);
});

test('« الكل » demande tout le journal : pas de borne basse', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    await page.setHistoryPeriod('all');

    const last = historyCalls(calls).at(-1);
    assert.doesNotMatch(last.endpoint, /from=/);
    assert.match(last.endpoint, /to=\d{4}-\d{2}-\d{2}/);
    assert.equal(page.historyPeriod, 'all');
});

test('changer de période relit l\'historique ; choisir la même période ne relit rien', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    const before = historyCalls(calls).length;

    await page.setHistoryPeriod('6m');
    assert.equal(historyCalls(calls).length, before, 'déjà sur cette période');

    await page.setHistoryPeriod('3m');
    assert.equal(historyCalls(calls).length, before + 1);
});

test('la courbe part du niveau d\'ouverture de la période, puis suit chaque jour de saisie', async () => {
    const { page } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    const chart = plain(page.historyChart);

    assert.equal(chart.dots.length, 3, 'ouverture + deux jours');
    assert.deepEqual(chart.dots.map((d) => d.v), [1.7, 2.5, 3.8], 'ouverture = 4 quarts sur 240');
    assert.ok(chart.dots[0].cx < chart.dots[1].cx && chart.dots[1].cx < chart.dots[2].cx);
});

test('sans niveau d\'ouverture (période antérieure au journal), la courbe ne commence qu\'à la première saisie', async () => {
    const { page } = boot({ history: { ...HISTORY, openingTotalQuarters: null } });
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    assert.equal(plain(page.historyChart).dots.length, 2, 'aucun zéro inventé');
});

test('un élève sans aucune évaluation affiche un état vide, pas une courbe plate', async () => {
    const { page } = boot({ history: { ...HISTORY, points: [], recent: [], openingTotalQuarters: null, firstEvaluationAt: null, currentTotalQuarters: 0 } });
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    assert.equal(page.historyEmpty, true);
    assert.equal(plain(page.historyChart).dots.length, 0);
});

test('l\'écran dit depuis quand le journal existe quand la période commence avant', async () => {
    const { page } = boot({ history: { ...HISTORY, firstEvaluationAt: '2026-08-01T00:00:00Z', openingTotalQuarters: null } });
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    assert.equal(page.historySince, '01/08/2026');
});

test('une erreur de chargement de l\'évolution est affichée sans bloquer la grille', async () => {
    const { page } = boot({ historyError: httpError(500) });
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    assert.equal(page.historyError, page.labels.historyError);
    assert.equal(page.historyLoading, false);
    assert.equal(page.grid.hizbs.length, 60, 'la grille de saisie reste utilisable');
    assert.equal(page.error, null);
});

test('enregistrer un Hizb recharge l\'évolution : le nouveau point apparaît', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);
    page.openDrawer(page.grid.hizbs[2]);
    page.pickQuarters(3);
    const before = historyCalls(calls).length;

    await page.save();

    assert.equal(historyCalls(calls).length, before + 1);
});

test('fermer l\'élève efface son évolution : le suivant ne montre pas celle du précédent', async () => {
    const { page } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    await page.closeStudent();

    assert.equal(page.history, null);
    assert.equal(page.historyError, null);
});

test('une évaluation se décrit en arabe : Hizb, passage d\'un état à l\'autre, note', () => {
    const { logic } = boot();
    const line = logic.describeEvaluation(HISTORY.recent[0]);

    assert.equal(line.title, 'الحزب 2');
    assert.equal(line.change, 'من الربع الأول إلى ثلاثة أرباع');
    assert.equal(line.rating, '4/5');
    assert.equal(line.by, 'Ahmad Lô');
    assert.equal(line.baseline, false);

    const start = logic.describeEvaluation(HISTORY.recent[1]);
    assert.equal(start.baseline, true);
    assert.equal(start.rating, null);
    assert.equal(start.by, null);
});

test('le pourcentage de quarts suit la règle du serveur (quarts sur 240, une décimale)', () => {
    const { logic } = boot();

    assert.equal(logic.percentOfQuarters(0), 0);
    assert.equal(logic.percentOfQuarters(4), 1.7);
    assert.equal(logic.percentOfQuarters(240), 100);
});

test('la courbe est fournie en SVG prêt à afficher, avec un cercle par point', async () => {
    const { page } = boot();
    await page.init();
    await page.openStudent(page.halqa.students[0]);

    const svg = page.historySvg;

    assert.match(svg, /^<svg /);
    assert.equal((svg.match(/<circle /g) || []).length, 3);
    assert.ok(svg.includes(page.labels.historyChart), 'titre accessible en arabe');
});
