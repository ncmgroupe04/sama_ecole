/**
 * Cahier de roulement (Franco-Arabe Bloc 5b) : logique pure (`window.rollBookLogic`) et composant `rollBookView`
 * (wwwroot/js/roll-book.js). Le serveur décide de tout (source d'une case, portée, taux) ; ces tests prouvent que
 * l'écran ne pose que des saisies EN ATTENTE bien formées, les envoie en un lot, ne laisse aucune trace d'un
 * retour à l'état serveur, désactive le futur, et affiche les libellés dans la langue choisie.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, flush, plain } from './harness.mjs';

const CLASSROOMS = [{ id: 'c1', name: 'CM2 A', level: 'Primaire' }];
const YEARS = [{ id: 'y1', label: '2026-2027', isActive: true }];
const TERMS = [{ id: 't1', label: '1er trimestre' }];

function counts(extra = {}) {
    return {
        present: 0, late: 0, partialAbsence: 0, justifiedAbsence: 0, unjustifiedAbsence: 0, recorded: 0, lateMinutes: 0,
        presenceRate: null, absenceRate: null, assiduityRate: null, coverage: null, ...extra
    };
}

function day(date, isClassDay = true, offReason = null, extra = {}) {
    return { date, dayOfWeek: new Date(`${date}T00:00:00Z`).getUTCDay(), isClassDay, offReason, closureLabel: null, closureLabelAr: null, ...extra };
}

function gridOf(cellsByStudent = {}) {
    return {
        classroomId: 'c1', classroomName: 'CM2 A', level: 'CM2', schoolYearId: 'y1', schoolYearLabel: '2026-2027',
        month: '2026-11', from: '2026-11-01', to: '2026-11-04',
        days: [day('2026-11-01', false, 'RestDay'), day('2026-11-02'), day('2026-11-03'), day('2026-11-04', false, 'Closure', { closureLabel: 'Vacances', closureLabelAr: 'عطلة' })],
        activeHalves: ['Morning', 'Afternoon'], classDays: 2, unplacedSessions: 0,
        students: [
            { studentId: 's1', matricule: 'E1', fullName: 'Awa Fall', fullNameAr: 'فاطمة فال', cells: cellsByStudent.s1 || [], counts: counts() },
            { studentId: 's2', matricule: 'E2', fullName: 'Binta Diop', fullNameAr: null, cells: cellsByStudent.s2 || [], counts: counts() }
        ],
        totals: counts()
    };
}

const cell = (date, halfDay, mark, source, extra = {}) =>
    ({ date, halfDay, mark, source, lateMinutes: 0, reason: null, entryId: source === 'Entry' ? 'e-' + date + halfDay : null, ...extra });

function boot({ role = 'Directeur', grid = gridOf(), coran = false, putResult = { created: 1, updated: 0, reverted: 0 }, failPut = null, today = '2026-11-20' } = {}) {
    const calls = [];
    const ctx = loadScripts(['roll-book.js'], {
        preload: {
            auth: { role },
            // Aperçu PDF partagé (pdf-preview.js) : doublure qui note ce qu'on lui demande d'ouvrir.
            pdfPreview: {
                state: () => ({
                    previews: [],
                    openPdfPreview(url, title, downloadName) { this.previews.push([url, title, downloadName]); }
                })
            },
            api: {
                get: async (endpoint) => {
                    calls.push(['GET', endpoint]);
                    if (endpoint === '/classrooms') return CLASSROOMS;
                    if (endpoint === '/school-years') return YEARS;
                    if (endpoint.startsWith('/school-years/')) return TERMS;
                    if (endpoint.startsWith('/roll-book/stats')) return { scope: 'class', students: [], classes: [], levels: [], totals: counts() };
                    if (endpoint.startsWith('/roll-book?')) return JSON.parse(JSON.stringify(grid));
                    return [];
                },
                put: async (endpoint, body) => {
                    calls.push(['PUT', endpoint, plain(body)]);
                    if (failPut) throw failPut;
                    return putResult;
                },
                toMessage: (_err, fallback) => fallback
            }
        }
    });
    // Le store schoolConfig n'est pas chargé dans ce bac à sable : on le simule (module Franco-Arabe).
    const original = ctx.sandbox.Alpine.store;
    ctx.sandbox.Alpine.store = (name, value) => (name === 'schoolConfig' && value === undefined ? { coranEnabled: coran } : original(name, value));
    const view = ctx.component('rollBookView');
    view.today = today;
    return { ctx, view, calls };
}

async function loaded(options) {
    const env = boot(options);
    await flush();
    env.view.classroomId = 'c1';
    env.view.month = '2026-11';
    await env.view.loadGrid();
    return env;
}

// -- Logique pure ------------------------------------------------------------------------------------

test('la rotation de clic enchaîne vide → présent → retard → justifié → non justifié → retour au calcul', () => {
    const { nextStatus } = boot().ctx.window.rollBookLogic;

    assert.equal(nextStatus('NotRecorded'), 'Present');
    assert.equal(nextStatus('Present'), 'Late');
    assert.equal(nextStatus('Late'), 'JustifiedAbsence');
    assert.equal(nextStatus('JustifiedAbsence'), 'UnjustifiedAbsence');
    assert.equal(nextStatus('UnjustifiedAbsence'), null);
    assert.equal(nextStatus('PartialAbsence'), 'Present', 'un constat dérivé n’est pas saisissable : le clic le remplace');
});

test('percent : virgule française, null → tiret (jamais un faux 100 %)', () => {
    const { percent } = boot().ctx.window.rollBookLogic;

    assert.equal(percent(0.875), '87,5 %');
    assert.equal(percent(1), '100 %');
    assert.equal(percent(0), '0 %');
    assert.equal(percent(null), '—');
    assert.equal(percent(undefined), '—');
});

test('currentMonth rend AAAA-MM en UTC', () => {
    const { currentMonth } = boot().ctx.window.rollBookLogic;

    assert.equal(currentMonth(new Date('2026-11-30T23:30:00Z')), '2026-11');
    assert.equal(currentMonth(new Date('2026-12-01T00:00:00Z')), '2026-12');
});

// -- Chargement --------------------------------------------------------------------------------------

test('au chargement, classes et périodes de l’année active sont lues', async () => {
    const { view, calls } = boot();
    await flush();

    assert.equal(view.classrooms.length, 1);
    assert.equal(view.terms.length, 1);
    assert.ok(calls.some((c) => c[1] === '/school-years/y1/terms'));
});

test('loadGrid interroge la classe et le mois, indexe les cases et vide les saisies en attente', async () => {
    const { view, calls } = await loaded({ grid: gridOf({ s1: [cell('2026-11-02', 'Morning', 'Present', 'Derived')] }) });

    assert.deepEqual(calls.at(-1), ['GET', '/roll-book?classroomId=c1&month=2026-11']);
    assert.equal(view.grid.students.length, 2);
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'Present');
    assert.equal(view.effective('s1', '2026-11-02', 'Afternoon').mark, 'NotRecorded');
    assert.equal(view.pendingCount, 0);
});

test('sans classe choisie, rien n’est demandé', async () => {
    const { view, calls } = boot();
    await flush();
    const before = calls.length;

    await view.loadGrid();

    assert.equal(calls.length, before);
});

test('les colonnes sont à plat : deux par jour de classe, une grisée par jour non travaillé', async () => {
    const { view } = await loaded();

    assert.deepEqual(plain(view.columns.map((c) => [c.date, c.half, c.off])), [
        ['2026-11-01', null, true],
        ['2026-11-02', 'Morning', false], ['2026-11-02', 'Afternoon', false],
        ['2026-11-03', 'Morning', false], ['2026-11-03', 'Afternoon', false],
        ['2026-11-04', null, true]
    ]);
});

// -- Saisie en attente -------------------------------------------------------------------------------

test('un clic sur une case vide pose « présent » en attente, sans rien envoyer', async () => {
    const { view, calls } = await loaded();
    const before = calls.length;

    view.cycle('s1', '2026-11-02', 'Morning');

    assert.equal(view.pendingCount, 1);
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'Present');
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').pending, true);
    assert.equal(calls.length, before, 'aucune requête avant « Enregistrer »');
});

test('cliquer en boucle parcourt la rotation, et revient à l’état serveur sans laisser de trace', async () => {
    const { view } = await loaded();

    ['Present', 'Late', 'JustifiedAbsence', 'UnjustifiedAbsence'].forEach((expected) => {
        view.cycle('s1', '2026-11-02', 'Morning');
        assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, expected);
    });

    view.cycle('s1', '2026-11-02', 'Morning');
    assert.equal(view.pendingCount, 0, 'retour au calcul sur une case sans saisie : rien à défaire');
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'NotRecorded');
});

test('sur une case qui a une saisie du cahier, « retour au calcul » est une saisie en attente à statut nul', async () => {
    const { view } = await loaded({ grid: gridOf({ s1: [cell('2026-11-02', 'Morning', 'UnjustifiedAbsence', 'Entry')] }) });

    view.cycle('s1', '2026-11-02', 'Morning');

    const pending = plain(view.pending['s1|2026-11-02|Morning']);
    assert.equal(pending.status, null);
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').reverting, true);
});

test('remettre la valeur déjà enregistrée supprime la saisie en attente', async () => {
    const { view } = await loaded({ grid: gridOf({ s1: [cell('2026-11-02', 'Morning', 'Present', 'Entry')] }) });

    view.setCell('s1', '2026-11-02', 'Morning', 'Late', { lateMinutes: 10 });
    assert.equal(view.pendingCount, 1);

    view.setCell('s1', '2026-11-02', 'Morning', 'Present');
    assert.equal(view.pendingCount, 0);
});

test('les minutes de retard ne sont gardées que pour le statut Retard', async () => {
    const { view } = await loaded();

    view.setCell('s1', '2026-11-02', 'Morning', 'Late', { lateMinutes: '15', reason: ' Bus ' });
    assert.deepEqual(plain(view.pending['s1|2026-11-02|Morning']),
        { studentId: 's1', date: '2026-11-02', halfDay: 'Morning', status: 'Late', lateMinutes: 15, reason: 'Bus' });

    view.setCell('s1', '2026-11-02', 'Morning', 'Present', { lateMinutes: 15 });
    assert.equal(view.pending['s1|2026-11-02|Morning'].lateMinutes, 0);
});

test('une case future est désactivée : ni clic ni clavier ne posent de saisie', async () => {
    const { view } = await loaded({ today: '2026-11-02' });

    assert.equal(view.isFuture('2026-11-03'), true);
    assert.equal(view.isFuture('2026-11-02'), false, 'aujourd’hui est modifiable');

    view.cycle('s1', '2026-11-03', 'Morning');
    view.onKey({ key: 'p', preventDefault() {} }, 's1', '2026-11-03', 'Morning');
    assert.equal(view.pendingCount, 0);
});

test('clavier : P, R, J, A posent un statut, Retour arrière revient au calcul', async () => {
    const { view } = await loaded({ grid: gridOf({ s2: [cell('2026-11-02', 'Afternoon', 'Present', 'Entry')] }) });
    const press = (key, id, half) => view.onKey({ key, preventDefault() {} }, id, '2026-11-02', half);

    press('p', 's1', 'Morning');
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'Present');
    press('R', 's1', 'Morning');
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'Late');
    press('j', 's1', 'Morning');
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'JustifiedAbsence');
    press('a', 's1', 'Morning');
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'UnjustifiedAbsence');

    press('Backspace', 's2', 'Afternoon');
    assert.equal(view.pending['s2|2026-11-02|Afternoon'].status, null);

    press('x', 's1', 'Morning');
    assert.equal(view.effective('s1', '2026-11-02', 'Morning').mark, 'UnjustifiedAbsence', 'une autre touche ne change rien');
});

test('le panneau de détail fixe statut, retard et motif de la case sélectionnée', async () => {
    const { view } = await loaded();

    view.select('s1', '2026-11-03', 'Afternoon');
    view.applyDetail('Late', 25, 'Panne de bus');

    assert.deepEqual(plain(view.pending['s1|2026-11-03|Afternoon']),
        { studentId: 's1', date: '2026-11-03', halfDay: 'Afternoon', status: 'Late', lateMinutes: 25, reason: 'Panne de bus' });
    assert.equal(view.selectedCell.mark, 'Late');
    assert.equal(view.selectedStudent.fullName, 'Awa Fall');
});

test('annuler les modifications vide les saisies en attente', async () => {
    const { view } = await loaded();
    view.cycle('s1', '2026-11-02', 'Morning');
    view.cycle('s2', '2026-11-03', 'Afternoon');

    view.discard();

    assert.equal(view.pendingCount, 0);
});

// -- Enregistrement ----------------------------------------------------------------------------------

test('enregistrer envoie UN lot atomique puis recharge la grille', async () => {
    const { view, calls } = await loaded({ putResult: { created: 2, updated: 0, reverted: 1 } });
    view.setCell('s1', '2026-11-02', 'Morning', 'Late', { lateMinutes: 12, reason: 'Bus' });
    view.setCell('s2', '2026-11-02', 'Afternoon', 'JustifiedAbsence');

    await view.save();

    const puts = calls.filter((c) => c[0] === 'PUT');
    assert.equal(puts.length, 1, 'un seul appel');
    assert.equal(puts[0][1], '/roll-book/entries');
    assert.equal(puts[0][2].classroomId, 'c1');
    assert.deepEqual(puts[0][2].entries, [
        { studentId: 's1', date: '2026-11-02', halfDay: 'Morning', status: 'Late', lateMinutes: 12, reason: 'Bus' },
        { studentId: 's2', date: '2026-11-02', halfDay: 'Afternoon', status: 'JustifiedAbsence', lateMinutes: 0, reason: null }
    ]);
    assert.equal(calls.at(-1)[0], 'GET', 'la grille est rechargée depuis le serveur');
    assert.equal(view.pendingCount, 0);
    assert.match(view.notice, /2 case\(s\) enregistrée\(s\), 1 remise\(s\) au calcul/);
});

test('le message d’enregistrement n’annonce que les compteurs non nuls', async () => {
    const onlyReverted = await loaded({ putResult: { created: 0, updated: 0, reverted: 2 } });
    onlyReverted.view.cycle('s1', '2026-11-02', 'Morning');
    await onlyReverted.view.save();
    assert.equal(onlyReverted.view.notice, '2 remise(s) au calcul.');

    const onlySaved = await loaded({ putResult: { created: 1, updated: 2, reverted: 0 } });
    onlySaved.view.cycle('s1', '2026-11-02', 'Morning');
    await onlySaved.view.save();
    assert.equal(onlySaved.view.notice, '3 case(s) enregistrée(s).');
});

test('un retour au calcul part avec un statut nul', async () => {
    const { view, calls } = await loaded({ grid: gridOf({ s1: [cell('2026-11-02', 'Morning', 'Present', 'Entry')] }) });
    view.setCell('s1', '2026-11-02', 'Morning', null);

    await view.save();

    assert.equal(calls.find((c) => c[0] === 'PUT')[2].entries[0].status, null);
});

test('rien à enregistrer : aucune requête', async () => {
    const { view, calls } = await loaded();

    await view.save();

    assert.equal(calls.some((c) => c[0] === 'PUT'), false);
});

test('si le serveur refuse le lot, les modifications restent à l’écran et l’erreur est affichée', async () => {
    const { view } = await loaded({ failPut: new Error('refus') });
    view.cycle('s1', '2026-11-02', 'Morning');

    await view.save();

    assert.equal(view.pendingCount, 1, 'le lot est atomique : rien n’a été écrit, rien n’est perdu côté écran');
    assert.match(view.error, /enregistrement/i);
    assert.equal(view.isSaving, false);
});

// -- Langue ------------------------------------------------------------------------------------------

test('sans module Franco-Arabe, l’écran reste en français', async () => {
    const { view } = boot({ coran: false });

    assert.equal(view.bilingualAvailable, false);
    assert.equal(view.lang, 'fr');
    assert.equal(view.t('present'), 'Présent');
});

test('avec le module Franco-Arabe, le bilingue est le défaut et les libellés suivent la langue', async () => {
    const { view } = boot({ coran: true });

    assert.equal(view.bilingualAvailable, true);
    assert.equal(view.lang, 'bi');
    assert.equal(view.t('present'), 'Présent · حاضر');
    assert.equal(view.short('present'), 'Présent', 'en-têtes courts : une seule langue');

    view.setLang('ar');
    assert.equal(view.t('present'), 'حاضر');
    assert.equal(view.rtl, true);
    assert.equal(view.glyph('JustifiedAbsence'), 'ع');
    assert.equal(view.markLabel('UnjustifiedAbsence'), 'غائب بدون عذر');

    view.setLang('fr');
    assert.equal(view.rtl, false);
    assert.equal(view.glyph('JustifiedAbsence'), 'AJ');
});

test('la langue par défaut suit le module même si le store de réglages se remplit après le montage', async () => {
    let coran = false;
    const env = boot();
    env.ctx.sandbox.Alpine.store = (name, value) => (name === 'schoolConfig' && value === undefined ? { coranEnabled: coran } : undefined);

    assert.equal(env.view.lang, 'fr', 'store pas encore prêt : français');
    assert.match(env.view.pdfUrl(), /lang=fr$/);

    coran = true;
    assert.equal(env.view.lang, 'bi', 'le module apparaît après coup : bilingue, sans rechargement');
    assert.match(env.view.pdfUrl(), /lang=bi$/);

    env.view.setLang('ar');
    assert.equal(env.view.lang, 'ar');
    coran = false;
    assert.equal(env.view.lang, 'fr', 'sans module, le choix mémorisé est ignoré');
});

test('la langue choisie est mémorisée pour le prochain affichage', async () => {
    const env = boot({ coran: true });
    env.view.setLang('ar');

    assert.equal(env.ctx.localStorage.getItem('rollbook.lang'), 'ar');
});

test('en arabe, le nom arabe de l’élève passe en premier, avec repli sur le français', async () => {
    const { view } = await loaded({ coran: true });
    view.setLang('ar');

    assert.equal(view.studentName(view.grid.students[0]), 'فاطمة فال');
    assert.equal(view.studentName(view.grid.students[1]), 'Binta Diop', 'pas de nom arabe : repli sur le français');

    view.setLang('fr');
    assert.equal(view.studentName(view.grid.students[0]), 'Awa Fall');
});

test('un jour de fermeture affiche son libellé, en arabe quand il existe', async () => {
    const { view } = await loaded({ coran: true });
    const closure = view.grid.days[3];

    view.setLang('fr');
    assert.equal(view.offLabel(closure), 'Vacances');
    view.setLang('ar');
    assert.equal(view.offLabel(closure), 'عطلة');
    assert.equal(view.offLabel(view.grid.days[0]), 'راحة');
    view.setLang('fr');
    assert.equal(view.offLabel(view.grid.days[0]), 'Repos');
});

test('les en-têtes de jour utilisent des chiffres latins même en arabe', async () => {
    const { view } = await loaded({ coran: true });
    view.setLang('ar');

    assert.equal(view.dayHeader(view.grid.days[1]).number, 2);
    assert.match(view.monthTitle(), /2026/, 'année en chiffres latins');
});

// -- Statistiques ------------------------------------------------------------------------------------

test('les statistiques ne sont proposées qu’à la direction et au secrétariat', () => {
    assert.equal(boot({ role: 'Directeur' }).view.canStats, true);
    assert.equal(boot({ role: 'Secretariat' }).view.canStats, true);
    assert.equal(boot({ role: 'Enseignant' }).view.canStats, false);
    assert.equal(boot({ role: 'Surveillant' }).view.canStats, false);
});

test('statistiques d’une classe sur le mois : portée, classe et mois dans la requête', async () => {
    const { view, calls } = await loaded();

    await view.showStats();

    assert.equal(view.tab, 'stats');
    assert.deepEqual(calls.at(-1), ['GET', '/roll-book/stats?scope=class&classroomId=c1&month=2026-11']);
});

test('statistiques par niveau sur une période d’évaluation : pas de classe, la période par son id', async () => {
    const { view, calls } = await loaded();
    view.statsScope = 'level';
    view.statsPeriod = 'term';
    view.statsTermId = 't1';

    await view.loadStats();

    assert.deepEqual(calls.at(-1), ['GET', '/roll-book/stats?scope=level&termId=t1']);
});

test('statistiques : rien n’est demandé sans classe (portée classe) ni période choisie', async () => {
    const { view, calls } = boot();
    await flush();
    const before = calls.length;

    await view.loadStats();
    view.statsScope = 'level';
    view.statsPeriod = 'term';
    await view.loadStats();

    assert.equal(calls.length, before);
});

test('un Enseignant ne déclenche aucune requête de statistiques', async () => {
    const { view, calls } = await loaded({ role: 'Enseignant' });
    const before = calls.length;

    await view.loadStats();

    assert.equal(calls.length, before);
});

// -- Export PDF --------------------------------------------------------------------------------------

test('l’export du mois ouvre l’aperçu PDF avec la classe, le mois et la langue de l’écran', async () => {
    const { view } = await loaded({ coran: true });
    view.setLang('ar');

    await view.exportMonthPdf();

    assert.deepEqual(plain(view.previews), [[
        '/api/v1/roll-book/pdf?classroomId=c1&month=2026-11&lang=ar', 'Cahier de roulement', 'Cahier-de-roulement-2026-11.pdf'
    ]]);
});

test('sans module Franco-Arabe, l’export est toujours demandé en français', async () => {
    const { view } = await loaded({ coran: false });

    assert.match(view.pdfUrl(), /lang=fr$/);
});

test('en bilingue, l’export est demandé en bilingue', async () => {
    const { view } = await loaded({ coran: true });
    view.setLang('bi');

    assert.match(view.pdfUrl(), /lang=bi$/);
});

test('l’export est refusé tant que des modifications ne sont pas enregistrées (le PDF relit la base)', async () => {
    const { view } = await loaded();
    view.cycle('s1', '2026-11-02', 'Morning');

    assert.equal(view.exportBlocked, true);
    await view.exportMonthPdf();
    assert.equal(view.previews.length, 0);

    view.discard();
    assert.equal(view.exportBlocked, false);
    await view.exportMonthPdf();
    assert.equal(view.previews.length, 1);
});

test('l’Enseignant saisit le cahier mais ne peut pas l’exporter', async () => {
    const { view } = await loaded({ role: 'Enseignant' });

    assert.equal(view.canExport, false);
    await view.exportMonthPdf();
    assert.equal(view.previews.length, 0);
});

test('le Surveillant exporte le cahier mais n’a pas les statistiques', () => {
    const { view } = boot({ role: 'Surveillant' });

    assert.equal(view.canExport, true);
    assert.equal(view.canStats, false);
});

test('l’export demande une classe et un mois', async () => {
    const { view } = boot();
    await flush();

    await view.exportMonthPdf();
    assert.equal(view.previews.length, 0, 'sans classe');

    view.classroomId = 'c1';
    view.month = '';
    await view.exportMonthPdf();
    assert.equal(view.previews.length, 0, 'sans mois');
});

test('l’export d’une période n’est proposé que pour une classe et un trimestre choisis', async () => {
    const { view } = await loaded();

    assert.equal(view.canExportTerm, false, 'période = mois');
    view.statsPeriod = 'term';
    assert.equal(view.canExportTerm, false, 'aucun trimestre choisi');
    view.statsTermId = 't1';
    assert.equal(view.canExportTerm, true);
    view.statsScope = 'level';
    assert.equal(view.canExportTerm, false, 'la portée niveau n’a pas de cahier de classe');
});

test('l’export d’une période envoie le trimestre par son id et nomme le fichier d’après son libellé', async () => {
    const { view } = await loaded({ coran: true });
    await flush();
    view.setLang('bi');
    view.statsPeriod = 'term';
    view.statsTermId = 't1';

    await view.exportTermPdf();

    assert.deepEqual(plain(view.previews), [[
        '/api/v1/roll-book/pdf?classroomId=c1&termId=t1&lang=bi', 'Cahier de roulement — période', 'Cahier-de-roulement-1er-trimestre.pdf'
    ]]);
});
