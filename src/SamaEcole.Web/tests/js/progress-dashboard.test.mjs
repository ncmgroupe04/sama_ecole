/**
 * Suivi coranique (/suivi-coranique) : logique pure (`window.progressDashboardLogic`) et composant
 * `progressDashboardPage` (wwwroot/js/progress-dashboard.js). Le serveur calcule tranches, moyennes et stagnation ;
 * ces tests prouvent que l'écran ne fait que les afficher, transmet le seuil choisi, n'invente pas de nombre de jours pour
 * un élève jamais évalué, et ouvre le bulletin PDF de l'élève visé.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts } from './harness.mjs';

const BANDS = [
    { min: 0, max: 0, studentCount: 1 },
    { min: 0, max: 10, studentCount: 4 },
    { min: 10, max: 25, studentCount: 2 },
    { min: 25, max: 50, studentCount: 0 },
    { min: 50, max: 75, studentCount: 0 },
    { min: 75, max: 100, studentCount: 1 }
];

function dashboard(overrides = {}) {
    return {
        generatedAt: '2026-10-06T12:00:00Z', staleDays: 30, studentsInHalqa: 8, unassignedStudents: 2,
        averageProgressPercent: 14.2, completedHizbs: 70, completedQuarters: 280, bands: BANDS,
        halqas: [{ instructorId: 'i1', instructorName: 'Serigne Modou', instructorNameAr: 'سيرين مودو', instructorStatus: 'Active', studentCount: 4, averageProgressPercent: 10.5, completedHizbs: 20, stagnantCount: 1 }],
        stagnantCount: 2,
        stagnant: [
            { studentId: 's1', matricule: 'ELEV-2026-0002', fullName: 'Binta Fall', fullNameAr: null, instructorId: 'i1', instructorName: 'Serigne Modou', lastEvaluatedAt: null, daysSinceEvaluation: null, progressPercent: 0 },
            { studentId: 's2', matricule: 'ELEV-2026-0003', fullName: 'Cheikh Ndiaye', fullNameAr: 'الشيخ', instructorId: 'i2', instructorName: 'Oustaz Fall', lastEvaluatedAt: '2026-08-07T10:00:00Z', daysSinceEvaluation: 60, progressPercent: 1.3 }
        ],
        ...overrides
    };
}

function boot(options = {}) {
    const calls = [];
    const ctx = loadScripts(['progress-dashboard.js'], {
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
                    if (options.error) throw options.error;
                    return options.data ?? dashboard();
                },
                toMessage: (err, fallback) => (err && err.message) || fallback
            }
        }
    });
    const components = ctx.initAlpine();
    return { page: components.get('progressDashboardPage')(), logic: ctx.window.progressDashboardLogic, calls };
}

// ---------------------------------------------------------------------------------------------- logique pure

test('chaque tranche a un libellé lisible', () => {
    const { logic } = boot();
    assert.deepEqual(BANDS.map((b) => logic.bandLabel(b)), ['0 %', 'jusqu\'à 10 %', '10 à 25 %', '25 à 50 %', '50 à 75 %', 'plus de 75 %']);
});

test('la barre d\'une tranche est relative à la plus peuplée', () => {
    const { logic } = boot();
    assert.equal(logic.barPercent(4, BANDS), 100, 'la plus grande tranche remplit la ligne');
    assert.equal(logic.barPercent(2, BANDS), 50);
    assert.equal(logic.barPercent(0, BANDS), 0);
    assert.equal(logic.barPercent(0, []), 0, 'aucune tranche : pas de division par zéro');
    assert.equal(logic.barPercent(3, [{ studentCount: 0 }]), 0, 'toutes les tranches vides : barres vides');
});

test('un élève jamais évalué n\'a pas de nombre de jours inventé', () => {
    const { logic } = boot();
    assert.equal(logic.stagnationLabel({ lastEvaluatedAt: null, daysSinceEvaluation: null }), 'Jamais évalué');
    assert.equal(logic.stagnationLabel({ lastEvaluatedAt: '2026-08-07', daysSinceEvaluation: 60 }), 'Il y a 60 jours');
    assert.equal(logic.stagnationLabel({ lastEvaluatedAt: '2026-10-05', daysSinceEvaluation: 1 }), 'Il y a 1 jour');
    assert.equal(logic.stagnationLabel({ lastEvaluatedAt: '2026-10-06', daysSinceEvaluation: 0 }), 'Évalué aujourd\'hui');
});

test('une alerte est forte quand l\'élève n\'a jamais été évalué ou dépasse deux fois le seuil', () => {
    const { logic } = boot();
    assert.equal(logic.isSevere({ lastEvaluatedAt: null, daysSinceEvaluation: null }, 30), true);
    assert.equal(logic.isSevere({ lastEvaluatedAt: 'x', daysSinceEvaluation: 60 }, 30), true);
    assert.equal(logic.isSevere({ lastEvaluatedAt: 'x', daysSinceEvaluation: 59 }, 30), false);
    assert.equal(logic.isSevere({ lastEvaluatedAt: 'x', daysSinceEvaluation: 31 }, 30), false);
});

test('le nom arabe est affiché avec repli sur le français', () => {
    const { logic } = boot();
    assert.equal(logic.pickName('الشيخ', 'Cheikh'), 'الشيخ');
    assert.equal(logic.pickName('  ', 'Cheikh'), 'Cheikh');
    assert.equal(logic.pickName(null, 'Cheikh'), 'Cheikh');
});

test('l\'URL et le nom du bulletin sont ceux de l\'élève, assainis', () => {
    const { logic } = boot();
    assert.equal(logic.reportUrl('abc-123'), '/api/v1/internat/students/abc-123/hizb-report/pdf');
    assert.equal(logic.reportFileName('ELEV-2026-0001'), 'Bulletin-Coranique-ELEV-2026-0001.pdf');
    assert.equal(logic.reportFileName('A/../B C'), 'Bulletin-Coranique-ABC.pdf', 'aucun séparateur de chemin dans le nom de fichier');
});

// ---------------------------------------------------------------------------------------------- composant

test('l\'ouverture charge le tableau de bord avec le seuil par défaut', async () => {
    const { page, calls } = boot();
    await page.init();

    assert.deepEqual(calls, [{ method: 'GET', endpoint: '/internat/progress-dashboard?staleDays=30' }]);
    assert.equal(page.data.studentsInHalqa, 8);
    assert.equal(page.loading, false);
    assert.equal(page.error, null);
});

test('changer le seuil recharge avec la nouvelle valeur', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.changeStaleDays('60');

    assert.equal(page.staleDays, 60);
    assert.equal(calls.at(-1).endpoint, '/internat/progress-dashboard?staleDays=60');
});

test('une erreur de chargement est affichée', async () => {
    const { page } = boot({ error: Object.assign(new Error('Panne'), { status: 500 }) });
    await page.init();

    assert.equal(page.error, 'Panne');
    assert.equal(page.data, null);
    assert.equal(page.loading, false);
});

test('une école sans élève en Halqa affiche l\'état vide', async () => {
    const { page } = boot({ data: dashboard({ studentsInHalqa: 0, stagnant: [], stagnantCount: 0, halqas: [] }) });
    await page.init();

    assert.equal(page.isEmpty, true);
});

test('une école avec des élèves en Halqa n\'est pas vide', async () => {
    const { page } = boot();
    await page.init();

    assert.equal(page.isEmpty, false);
});

test('les alertes au-delà du plafond sont annoncées, pas cachées', async () => {
    const { page } = boot({ data: dashboard({ stagnantCount: 130 }) });
    await page.init();

    assert.equal(page.hiddenStagnant, 128, '130 au total, 2 affichés');
});

test('aucune alerte masquée quand tout est affiché', async () => {
    const { page } = boot();
    await page.init();

    assert.equal(page.hiddenStagnant, 0);
});

test('« PDF » ouvre le bulletin de l\'élève visé dans la modale commune', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.openReport(page.data.stagnant[1]);

    assert.deepEqual(calls.find((c) => c.method === 'PDF'), {
        method: 'PDF',
        url: '/api/v1/internat/students/s2/hizb-report/pdf',
        title: 'Bulletin coranique — Cheikh Ndiaye',
        downloadName: 'Bulletin-Coranique-ELEV-2026-0003.pdf'
    });
});

test('la sévérité d\'une alerte suit le seuil choisi', async () => {
    const { page } = boot();
    await page.init();

    assert.equal(page.severe(page.data.stagnant[0]), true, 'jamais évalué');
    assert.equal(page.severe(page.data.stagnant[1]), true, '60 jours, seuil 30');
    await page.changeStaleDays('90');
    assert.equal(page.severe(page.data.stagnant[1]), false, '60 jours, seuil 90');
});
