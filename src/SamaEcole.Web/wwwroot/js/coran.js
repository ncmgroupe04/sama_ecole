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

        // --- Modale Suivi de mémorisation --------------------------------------------------

        progressModal: { open: false, mode: 'create', studentId: null, studentName: '', entryId: null, juzNumber: '', hizbNumber: '', surahNumber: '', status: 'InProcess', evaluationDate: '', notes: '', rowVersion: null, saving: false, error: null },

        openCreateProgressModal(row) {
            this.progressModal = { open: true, mode: 'create', studentId: row.studentId, studentName: row.fullName, entryId: null, juzNumber: '', hizbNumber: '', surahNumber: '', status: 'InProcess', evaluationDate: '', notes: '', rowVersion: null, saving: false, error: null };
        },

        openEditProgressModal(row, entry) {
            this.progressModal = { open: true, mode: 'edit', studentId: row.studentId, studentName: row.fullName, entryId: entry.id, juzNumber: entry.juzNumber, hizbNumber: entry.hizbNumber, surahNumber: entry.surahNumber, status: entry.status, evaluationDate: entry.evaluationDate || '', notes: entry.notes || '', rowVersion: entry.rowVersion, saving: false, error: null };
        },

        closeProgressModal() {
            this.progressModal.open = false;
        },

        /** Bornes reflétées du serveur (CreateQuranProgressCommandValidator) — évite un aller-retour
         *  pour une erreur triviale ; le serveur reste seul autorité (422 sinon). Seule la CRÉATION
         *  porte Juz/Hizb/Sourate : ils sont immuables et non affichés en édition. */
        progressValidationError() {
            const m = this.progressModal;
            if (m.mode === 'create') {
                const juz = Number(m.juzNumber), hizb = Number(m.hizbNumber), surah = Number(m.surahNumber);
                if (!Number.isInteger(juz) || juz < 1 || juz > 30) return 'Le Juz doit être compris entre 1 et 30.';
                if (!Number.isInteger(hizb) || hizb < 1 || hizb > 60) return 'Le Hizb doit être compris entre 1 et 60.';
                if (!Number.isInteger(surah) || surah < 1 || surah > 114) return 'La Sourate doit être comprise entre 1 et 114.';
            }
            return null;
        },

        async saveProgress() {
            const m = this.progressModal;
            const validationError = this.progressValidationError();
            if (validationError) { m.error = validationError; return; }

            m.saving = true;
            m.error = null;
            try {
                if (m.mode === 'create') {
                    await window.api.post('/quran/progress', {
                        studentId: m.studentId,
                        juzNumber: Number(m.juzNumber),
                        hizbNumber: Number(m.hizbNumber),
                        surahNumber: Number(m.surahNumber),
                        status: m.status,
                        evaluationDate: m.evaluationDate || null,
                        notes: m.notes || null
                    });
                } else {
                    await window.api.put(`/quran/progress/${m.entryId}`, {
                        status: m.status,
                        evaluationDate: m.evaluationDate || null,
                        notes: m.notes || null,
                        rowVersion: m.rowVersion
                    });
                }
                this.closeProgressModal();
                await this.loadProgress();
            } catch (err) {
                m.error = err && err.status === 409
                    ? 'Cette observation a été modifiée par un autre utilisateur.'
                    : window.api.toMessage(err, 'Une erreur est survenue.');
            } finally {
                m.saving = false;
            }
        },

        // --- Modale Évaluation orale -----------------------------------------------------------

        evaluationModal: { open: false, mode: 'create', studentId: null, studentName: '', entryId: null, evaluationDate: '', memoryMistakes: '0', tajwidMistakes: '0', hesitations: '0', finalScore: '', rowVersion: null, saving: false, error: null },

        todayIso() {
            return new Date().toISOString().slice(0, 10);
        },

        openCreateEvaluationModal(row) {
            this.evaluationModal = { open: true, mode: 'create', studentId: row.studentId, studentName: row.fullName, entryId: null, evaluationDate: this.todayIso(), memoryMistakes: '0', tajwidMistakes: '0', hesitations: '0', finalScore: '', rowVersion: null, saving: false, error: null };
        },

        openEditEvaluationModal(row, entry) {
            this.evaluationModal = { open: true, mode: 'edit', studentId: row.studentId, studentName: row.fullName, entryId: entry.id, evaluationDate: entry.evaluationDate, memoryMistakes: String(entry.memoryMistakes), tajwidMistakes: String(entry.tajwidMistakes), hesitations: String(entry.hesitations), finalScore: String(entry.finalScore), rowVersion: entry.rowVersion, saving: false, error: null };
        },

        closeEvaluationModal() {
            this.evaluationModal.open = false;
        },

        /** Bornes reflétées du serveur (CreateQuranEvaluationCommandValidator) — voir progressValidationError. */
        evaluationValidationError() {
            const m = this.evaluationModal;
            if (!m.evaluationDate) return 'La date est obligatoire.';
            if (m.evaluationDate > this.todayIso()) return 'La date ne peut pas être future.';
            const memory = Number(m.memoryMistakes), tajwid = Number(m.tajwidMistakes), hesitations = Number(m.hesitations), score = Number(m.finalScore);
            if (!Number.isInteger(memory) || memory < 0) return 'Le nombre d\'erreurs de mémoire doit être positif ou nul.';
            if (!Number.isInteger(tajwid) || tajwid < 0) return 'Le nombre d\'erreurs de tajwid doit être positif ou nul.';
            if (!Number.isInteger(hesitations) || hesitations < 0) return 'Le nombre d\'hésitations doit être positif ou nul.';
            if (Number.isNaN(score) || score < 0) return 'La note finale doit être positive ou nulle.';
            return null;
        },

        async saveEvaluation() {
            const m = this.evaluationModal;
            const validationError = this.evaluationValidationError();
            if (validationError) { m.error = validationError; return; }

            m.saving = true;
            m.error = null;
            try {
                const payload = {
                    evaluationDate: m.evaluationDate,
                    memoryMistakes: Number(m.memoryMistakes),
                    tajwidMistakes: Number(m.tajwidMistakes),
                    hesitations: Number(m.hesitations),
                    finalScore: Number(m.finalScore)
                };
                if (m.mode === 'create') {
                    await window.api.post('/quran/evaluations', { studentId: m.studentId, ...payload });
                } else {
                    await window.api.put(`/quran/evaluations/${m.entryId}`, { ...payload, rowVersion: m.rowVersion });
                }
                this.closeEvaluationModal();
                await this.loadEvaluations();
            } catch (err) {
                m.error = err && err.status === 409
                    ? 'Cette observation a été modifiée par un autre utilisateur.'
                    : window.api.toMessage(err, 'Une erreur est survenue.');
            } finally {
                m.saving = false;
            }
        }
    }));
});
