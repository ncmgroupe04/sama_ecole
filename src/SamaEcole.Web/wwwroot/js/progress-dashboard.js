/**
 * Suivi coranique (/suivi-coranique) — la vue de la Direction sur la mémorisation de l'établissement : progression
 * globale, répartition des élèves par tranche d'avancement, synthèse par Halqa, et alertes de STAGNATION (élèves non
 * évalués depuis N jours, ou jamais évalués), avec le bulletin coranique PDF de chaque élève.
 *
 * Consomme GET /api/v1/internat/progress-dashboard?staleDays=N (Directeur, Secrétariat, Surveillant) et ouvre le
 * bulletin par la modale PDF commune (pdf-preview.js) sur GET /api/v1/internat/students/{id}/hizb-report/pdf.
 *
 * Tout est calculé par le serveur — tranches, moyennes, stagnation, jamais d'évaluation. Ce fichier n'affiche que ce
 * qu'il renvoie. Il n'y a volontairement PAS de courbe d'évolution dans le temps : le suivi conserve un état courant par
 * Hizb, pas un historique, donc une courbe serait inventée. La logique pure d'affichage vit dans
 * `window.progressDashboardLogic`, testée sans navigateur (tests/js/progress-dashboard.test.mjs).
 */
(function () {
    'use strict';

    const DEFAULT_STALE_DAYS = 30;
    const STALE_OPTIONS = [14, 30, 60, 90];

    /** Libellé d'une tranche. La tranche 0-0 ne contient que les élèves à 0 % ; les autres sont (min, max]. */
    function bandLabel(band) {
        if (band.min === band.max) return '0 %';
        if (band.min === 0) return 'jusqu\'à ' + band.max + ' %';
        if (band.max === 100) return 'plus de ' + band.min + ' %';
        return band.min + ' à ' + band.max + ' %';
    }

    /** Largeur (0 à 100) de la barre d'une tranche, relative à la plus peuplée : la plus grande barre remplit la ligne. */
    function barPercent(count, bands) {
        const max = Math.max(0, ...(bands || []).map((b) => b.studentCount));
        return max === 0 ? 0 : Math.round((count / max) * 100);
    }

    /** « Jamais évalué » plutôt qu'un nombre de jours inventé quand l'élève n'a aucune évaluation. */
    function stagnationLabel(student) {
        if (student.lastEvaluatedAt == null || student.daysSinceEvaluation == null) return 'Jamais évalué';
        const days = student.daysSinceEvaluation;
        return days <= 0 ? 'Évalué aujourd\'hui' : 'Il y a ' + days + (days === 1 ? ' jour' : ' jours');
    }

    /** Alerte « forte » : jamais évalué, ou sans évaluation depuis plus de deux fois le seuil. */
    function isSevere(student, staleDays) {
        return student.lastEvaluatedAt == null || student.daysSinceEvaluation == null || student.daysSinceEvaluation >= staleDays * 2;
    }

    function pickName(arabic, french) {
        const ar = typeof arabic === 'string' ? arabic.trim() : '';
        return ar !== '' ? ar : (french || '');
    }

    function reportUrl(studentId) {
        return `/api/v1/internat/students/${studentId}/hizb-report/pdf`;
    }

    function reportFileName(matricule) {
        return 'Bulletin-Coranique-' + String(matricule || '').replace(/[^A-Za-z0-9_-]/g, '') + '.pdf';
    }

    window.progressDashboardLogic = {
        DEFAULT_STALE_DAYS, STALE_OPTIONS, bandLabel, barPercent, stagnationLabel, isSevere, pickName, reportUrl, reportFileName
    };

    document.addEventListener('alpine:init', () => {
        Alpine.data('progressDashboardPage', () => ({
            loading: true,
            error: null,
            data: null,
            staleDays: DEFAULT_STALE_DAYS,
            staleOptions: STALE_OPTIONS,

            // Modale de prévisualisation PDF commune (wwwroot/js/pdf-preview.js).
            ...window.pdfPreview.state(),

            bandLabel,
            stagnationLabel,
            pickName,

            async init() {
                await this.load();
            },

            async load() {
                this.loading = true;
                this.error = null;
                try {
                    this.data = await window.api.get(`/internat/progress-dashboard?staleDays=${this.staleDays}`);
                } catch (err) {
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement du suivi coranique.');
                } finally {
                    this.loading = false;
                }
            },

            async changeStaleDays(days) {
                this.staleDays = Number(days);
                await this.load();
            },

            get isEmpty() {
                return !!this.data && this.data.studentsInHalqa === 0;
            },

            get hiddenStagnant() {
                return this.data ? Math.max(0, this.data.stagnantCount - this.data.stagnant.length) : 0;
            },

            bandBar(count) {
                return barPercent(count, this.data ? this.data.bands : []);
            },

            severe(student) {
                return isSevere(student, this.staleDays);
            },

            async openReport(student) {
                await this.openPdfPreview(
                    reportUrl(student.studentId),
                    'Bulletin coranique — ' + student.fullName,
                    reportFileName(student.matricule)
                );
            }
        }));
    });
})();
