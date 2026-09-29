/**
 * Écran Coran/Franco-Arabe (/coran) — Phase 3, spec docs/superpowers/specs/2026-09-29-coran-screen-design.md.
 *
 * Un sélecteur de classe charge en parallèle GET /quran/progress/classroom/{id} et
 * GET /quran/evaluations/classroom/{id} (une ligne par élève, sa liste d'observations) —
 * QuranController, réservé par [RequireModule(SchoolModule.Coran)] côté serveur. Deux onglets
 * indépendants : l'échec de l'un n'empêche pas l'affichage de l'autre (Task 3).
 *
 * Écriture (ajout/correction) : Directeur + Enseignant (QuranController.WriteRoles). Lecture : +
 * Secrétariat — décision déjà actée en Phase 2, aucune garde JS nouvelle au-delà du masquage des
 * boutons (la vraie garde reste le serveur, 403/422/409).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('coranPage', () => ({
        canWrite: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant',

        classrooms: [],
        selectedClassroomId: '',
        activeTab: 'progress', // 'progress' | 'evaluations'

        loadingClassrooms: true,
        // Consommé par le partiel _ErrorBanner partagé (x-show="error") : échec du chargement des classes.
        error: null,

        async init() {
            this.loadingClassrooms = true;
            this.error = null;
            try {
                this.classrooms = await window.api.get('/classrooms');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des classes.');
            } finally {
                this.loadingClassrooms = false;
            }
        },

        async selectClassroom(id) {
            this.selectedClassroomId = id;
            if (!id) {
                this.progressRows = [];
                this.evaluationRows = [];
                return;
            }
            // Indépendants : un onglet en échec n'empêche pas l'affichage de l'autre.
            await Promise.all([this.loadProgress(), this.loadEvaluations()]);
        },

        progressRows: [],
        loadingProgress: false,
        progressError: null,

        evaluationRows: [],
        loadingEvaluations: false,
        evaluationError: null,

        async loadProgress() {
            if (!this.selectedClassroomId) return;
            this.loadingProgress = true;
            this.progressError = null;
            try {
                const rows = await window.api.get(`/quran/progress/classroom/${this.selectedClassroomId}`);
                this.progressRows = rows.map(r => ({ ...r, expanded: false }));
            } catch (err) {
                this.progressError = window.api.toMessage(err, 'Erreur lors du chargement du suivi de mémorisation.');
            } finally {
                this.loadingProgress = false;
            }
        },

        async loadEvaluations() {
            if (!this.selectedClassroomId) return;
            this.loadingEvaluations = true;
            this.evaluationError = null;
            try {
                const rows = await window.api.get(`/quran/evaluations/classroom/${this.selectedClassroomId}`);
                this.evaluationRows = rows.map(r => ({ ...r, expanded: false }));
            } catch (err) {
                this.evaluationError = window.api.toMessage(err, 'Erreur lors du chargement des évaluations orales.');
            } finally {
                this.loadingEvaluations = false;
            }
        },

        toggleExpand(row) {
            row.expanded = !row.expanded;
        },

        /** Entrées triées par date décroissante ; une entrée SANS date (QuranProgress.evaluationDate
         *  nullable) passe en dernier — jamais confondue avec la plus récente. */
        sortedProgressEntries(entries) {
            return [...entries].sort((a, b) => {
                if (!a.evaluationDate && !b.evaluationDate) return 0;
                if (!a.evaluationDate) return 1;
                if (!b.evaluationDate) return -1;
                return b.evaluationDate.localeCompare(a.evaluationDate);
            });
        },

        /** QuranEvaluation.evaluationDate est TOUJOURS renseignée (champ requis, pas nullable) : tri
         *  direct, sans le cas "sans date" de sortedProgressEntries. */
        sortedEvaluationEntries(entries) {
            return [...entries].sort((a, b) => b.evaluationDate.localeCompare(a.evaluationDate));
        },

        latestProgress(row) {
            const sorted = this.sortedProgressEntries(row.entries);
            return sorted.length > 0 ? sorted[0] : null;
        },

        latestEvaluation(row) {
            const sorted = this.sortedEvaluationEntries(row.entries);
            return sorted.length > 0 ? sorted[0] : null;
        },

        statusLabel(status) {
            return { InProcess: 'En cours', Memorized: 'Mémorisé', Revised: 'Révisé' }[status] || status;
        },
    }));
});
