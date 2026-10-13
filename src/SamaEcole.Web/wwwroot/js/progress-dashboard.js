/**
 * Suivi coranique (/suivi-coranique) — la vue de la Direction sur la mémorisation de l'établissement : progression
 * globale, répartition des élèves par tranche d'avancement, synthèse par Halqa, et alertes de STAGNATION (élèves non
 * évalués depuis N jours, ou jamais évalués), avec le bulletin coranique PDF de chaque élève.
 *
 * Consomme GET /api/v1/internat/progress-dashboard?staleDays=N (Directeur, Secrétariat, Surveillant) et ouvre le
 * bulletin par la modale PDF commune (pdf-preview.js) sur GET /api/v1/internat/students/{id}/hizb-report/pdf.
 * Le bulletin est accessible de deux endroits : la liste des alertes (élèves à relancer) ET chaque Halqa dépliée dans
 * la synthèse (GET /api/v1/internat/instructors/{id}/students) — sans ce second accès, un élève à jour de ses
 * évaluations n'aurait aucun bulletin atteignable depuis l'écran.
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

    /** Périodes de la courbe de progression mensuelle (le serveur plafonne à 24 mois). */
    const TREND_PERIODS = [
        { key: '3m', label: '3 mois' },
        { key: '6m', label: '6 mois' },
        { key: '12m', label: '12 mois' },
        { key: '24m', label: '24 mois' }
    ];

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
    /**
     * Date de dernière évaluation d'un élève d'une Halqa (la liste d'une Halqa ne renvoie pas de nombre de jours, à la différence
     * des alertes de stagnation). Calée sur UTC, comme le serveur : la date ne dépend pas du fuseau du poste.
     */
    function evaluationDateLabel(lastEvaluatedAt) {
        if (lastEvaluatedAt == null) return 'Jamais évalué';
        return new Date(lastEvaluatedAt).toLocaleDateString('fr-FR', { timeZone: 'UTC' });
    }

    function isSevere(student, staleDays) {
        return student.lastEvaluatedAt == null || student.daysSinceEvaluation == null || student.daysSinceEvaluation >= staleDays * 2;
    }

    /** Libellé d'un mois (« oct. 2026 »), calé sur UTC comme le serveur. */
    function monthLabel(monthStart) {
        return new Date(monthStart + 'T00:00:00Z').toLocaleDateString('fr-FR', { month: 'short', year: 'numeric', timeZone: 'UTC' });
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
        DEFAULT_STALE_DAYS, STALE_OPTIONS, TREND_PERIODS, bandLabel, barPercent, stagnationLabel, evaluationDateLabel, monthLabel, isSevere, pickName, reportUrl, reportFileName
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

            // Élèves de chaque Halqa dépliée, par identifiant d'Oustaz : { open, loading, error, students }.
            halqaPanels: {},

            // Progression moyenne par mois (journal des évaluations), école entière ou une Halqa. Indépendante du seuil de stagnation.
            trend: null,
            trendLoading: false,
            trendError: null,
            trendPeriod: '6m',
            trendInstructorId: '',
            trendPeriods: TREND_PERIODS,

            bandLabel,
            stagnationLabel,
            evaluationDateLabel,
            monthLabel,
            pickName,

            async init() {
                await this.load();
                await this.loadTrend();
            },

            async loadTrend() {
                const { from, to } = window.trendChart.periodRange(this.trendPeriod);
                this.trendLoading = true;
                this.trendError = null;
                try {
                    const instructor = this.trendInstructorId ? '&instructorId=' + this.trendInstructorId : '';
                    this.trend = await window.api.get(`/internat/progress-history?from=${from}&to=${to}${instructor}`);
                } catch (err) {
                    this.trendError = window.api.toMessage(err, 'Erreur lors du chargement de la progression mensuelle.');
                } finally {
                    this.trendLoading = false;
                }
            },

            async setTrendPeriod(key) {
                if (key === this.trendPeriod) return;
                this.trendPeriod = key;
                await this.loadTrend();
            },

            async setTrendInstructor(instructorId) {
                this.trendInstructorId = instructorId || '';
                await this.loadTrend();
            },

            /** Un point par mois renvoyé : la progression moyenne (en % du Coran) à la fin de chaque mois. */
            get trendChart() {
                const months = this.trend ? this.trend.months : [];
                const at = (monthStart) => Date.parse(monthStart + 'T12:00:00Z');
                return window.trendChart.build(
                    months.map((m) => ({ t: at(m.monthStart), v: m.averageProgressPercent })),
                    { formatX: (t) => monthLabel(new Date(t).toISOString().slice(0, 10)) });
            },

            get trendSvg() {
                return window.trendChart.svg(this.trendChart, { label: 'Progression moyenne par mois' });
            },

            /** Pas de mois à tracer : journal encore vide ou période antérieure à son démarrage. */
            get trendEmpty() {
                return !!this.trend && this.trend.months.length === 0;
            },

            /** Date de début du journal : avant elle, aucun historique n'existe (on ne trace pas de 0 % inventé). */
            get trendSince() {
                return this.trend && this.trend.firstEvaluationAt ? evaluationDateLabel(this.trend.firstEvaluationAt) : null;
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

            // --- Élèves d'une Halqa --------------------------------------------------------------------
            // La liste des « Élèves à relancer » ne montre que ceux qui n'ont pas été évalués récemment : sans cette liste,
            // un élève à jour n'aurait aucun bulletin accessible. On déplie donc la Halqa pour tous ses élèves.

            halqaPanel(instructorId) {
                return this.halqaPanels[instructorId] || { open: false, loading: false, error: null, students: [] };
            },

            isHalqaOpen(instructorId) {
                return this.halqaPanel(instructorId).open;
            },

            /** Déplie (en relisant la liste : une évaluation a pu changer) ou replie la Halqa. Replier ne lit rien. */
            async toggleHalqa(halqa) {
                const id = halqa.instructorId;
                if (this.isHalqaOpen(id)) {
                    this.halqaPanels[id] = { ...this.halqaPanel(id), open: false };
                    return;
                }
                this.halqaPanels[id] = { open: true, loading: true, error: null, students: this.halqaPanel(id).students };
                try {
                    const result = await window.api.get(`/internat/instructors/${id}/students`);
                    this.halqaPanels[id] = { open: true, loading: false, error: null, students: result.students };
                } catch (err) {
                    this.halqaPanels[id] = {
                        open: true, loading: false, students: [],
                        error: window.api.toMessage(err, 'Erreur lors du chargement des élèves de cette Halqa.')
                    };
                }
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
