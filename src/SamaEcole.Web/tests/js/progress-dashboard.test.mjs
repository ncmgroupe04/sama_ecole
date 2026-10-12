/**
 * Suivi coranique (/suivi-coranique) : logique pure (`window.progressDashboardLogic`) et composant
 * `progressDashboardPage` (wwwroot/js/progress-dashboard.js). Le serveur calcule tranches, moyennes et stagnation ;
 * ces tests prouvent que l'écran ne fait que les afficher, transmet le seuil choisi, n'invente pas de nombre de jours pour
 * un élève jamais évalué, et ouvre le bulletin PDF de l'élève visé.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadScripts, plain } from './harness.mjs';

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

function halqaOf(overrides = {}) {
    return {
        instructorId: 'i1', instructorName: 'Serigne Modou', instructorNameAr: 'سيرين مودو', studentCount: 2, averageProgressPercent: 5.4,
        students: [
            { studentId: 's10', matricule: 'ELEV-2026-0010', fullName: 'Awa Diop', fullNameAr: 'عائشة جوب', completedHizbs: 3, inProgressHizbs: 1, completedQuarters: 13, progressPercent: 5.4, lastEvaluatedAt: '2026-10-01T10:00:00Z' },
            { studentId: 's11', matricule: 'ELEV-2026-0011', fullName: 'Moussa Ba', fullNameAr: null, completedHizbs: 0, inProgressHizbs: 0, completedQuarters: 0, progressPercent: 0, lastEvaluatedAt: null }
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
                    if (endpoint.startsWith('/internat/instructors/')) {
                        if (options.halqaError) throw options.halqaError;
                        return options.halqa ?? halqaOf();
                    }
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

// ------------------------------------------------------------------------- bulletin de TOUS les élèves d'une Halqa

test('ouvrir une Halqa liste tous ses élèves, y compris ceux qui n\'ont rien à relancer', async () => {
    const { page, calls } = boot();
    await page.init();

    await page.toggleHalqa(page.data.halqas[0]);

    assert.ok(calls.some((c) => c.method === 'GET' && c.endpoint === '/internat/instructors/i1/students'));
    assert.equal(page.isHalqaOpen('i1'), true);
    const students = page.halqaPanel('i1').students;
    assert.deepEqual(students.map((s) => s.studentId), ['s10', 's11']);
    assert.ok(!page.data.stagnant.some((s) => s.studentId === 's10'), 's10 n\'est pas dans les alertes : c\'est tout l\'objet');
});

test('refermer une Halqa ne relance aucune requête, la rouvrir relit la liste', async () => {
    const { page, calls } = boot();
    await page.init();
    const reads = () => calls.filter((c) => c.endpoint === '/internat/instructors/i1/students').length;

    await page.toggleHalqa(page.data.halqas[0]);
    await page.toggleHalqa(page.data.halqas[0]);
    assert.equal(page.isHalqaOpen('i1'), false);
    assert.equal(reads(), 1, 'refermer ne lit rien');

    await page.toggleHalqa(page.data.halqas[0]);
    assert.equal(page.isHalqaOpen('i1'), true);
    assert.equal(reads(), 2, 'une évaluation a pu changer : on relit');
});

test('une Halqa dont le chargement échoue affiche son erreur sans casser le tableau', async () => {
    const { page } = boot({ halqaError: Object.assign(new Error('HTTP 500'), { status: 500 }) });
    await page.init();

    await page.toggleHalqa(page.data.halqas[0]);

    assert.equal(page.isHalqaOpen('i1'), true, 'la ligne reste ouverte pour montrer l\'erreur');
    assert.equal(page.halqaPanel('i1').error, 'HTTP 500');
    assert.equal(page.halqaPanel('i1').loading, false);
    assert.equal(page.error, null, 'le tableau de bord, lui, reste affiché');
    assert.ok(page.data, 'les chiffres globaux sont conservés');
});

test('la dernière évaluation d\'un élève de Halqa s\'affiche en date, ou « Jamais évalué »', () => {
    const { logic } = boot();
    assert.equal(logic.evaluationDateLabel('2026-10-01T10:00:00Z'), '01/10/2026');
    assert.equal(logic.evaluationDateLabel('2026-10-01T23:30:00Z'), '01/10/2026', 'la date est celle du serveur (UTC), pas celle du poste');
    assert.equal(logic.evaluationDateLabel(null), 'Jamais évalué');
    assert.equal(logic.evaluationDateLabel(undefined), 'Jamais évalué');
});

test('une Halqa jamais ouverte a un panneau vide, sans erreur', async () => {
    const { page } = boot();
    await page.init();

    assert.equal(page.isHalqaOpen('i1'), false);
    assert.deepEqual(plain(page.halqaPanel('i1')), { open: false, loading: false, error: null, students: [] });
});

test('« PDF » d\'un élève de la Halqa ouvre SON bulletin, même s\'il est à jour', async () => {
    const { page, calls } = boot();
    await page.init();
    await page.toggleHalqa(page.data.halqas[0]);

    await page.openReport(page.halqaPanel('i1').students[0]);

    assert.deepEqual(calls.find((c) => c.method === 'PDF'), {
        method: 'PDF',
        url: '/api/v1/internat/students/s10/hizb-report/pdf',
        title: 'Bulletin coranique — Awa Diop',
        downloadName: 'Bulletin-Coranique-ELEV-2026-0010.pdf'
    });
});

test('deux Halqa ouvertes ensemble ne se mélangent pas', async () => {
    const { page } = boot({ data: dashboard({ halqas: [
        { instructorId: 'i1', instructorName: 'A', instructorNameAr: null, instructorStatus: 'Active', studentCount: 2, averageProgressPercent: 5, completedHizbs: 3, stagnantCount: 0 },
        { instructorId: 'i2', instructorName: 'B', instructorNameAr: null, instructorStatus: 'Active', studentCount: 2, averageProgressPercent: 5, completedHizbs: 3, stagnantCount: 0 }
    ] }) });
    await page.init();

    await page.toggleHalqa(page.data.halqas[0]);
    await page.toggleHalqa(page.data.halqas[1]);
    await page.toggleHalqa(page.data.halqas[0]);

    assert.equal(page.isHalqaOpen('i1'), false);
    assert.equal(page.isHalqaOpen('i2'), true, 'refermer l\'une n\'affecte pas l\'autre');
});
