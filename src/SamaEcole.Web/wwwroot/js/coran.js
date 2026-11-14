/**
 * Écran Coran (/coran) — suivi de mémorisation (QuranProgress) et évaluations orales (QuranEvaluation) d'une classe,
 * sur l'API /api/v1/quran (module Coran, SchoolSettings.IsCoranModuleEnabled). Spécification :
 * docs/superpowers/specs/2026-09-29-coran-screen-design.md.
 *
 * Une classe, deux onglets, une ligne par élève avec sa dernière observation et son historique dépliable. La saisie et la
 * correction passent par une modale (les champs ne tiennent pas dans une cellule). Écriture : Directeur et Enseignant ;
 * lecture : + Secrétariat (le serveur reste la barrière, 403). Aucune règle métier ici : les bornes affichées reprennent
 * celles du serveur pour éviter un aller-retour trivial, mais la validation SERVEUR fait autorité (422 affiché tel quel).
 */
document.addEventListener('alpine:init', () => {
    const STATUS_LABELS = { InProcess: 'En cours', Memorized: 'Mémorisé', Revised: 'Révisé' };
    const STATUS_OPTIONS = [
        { value: 'InProcess', label: 'En cours' },
        { value: 'Memorized', label: 'Mémorisé' },
        { value: 'Revised', label: 'Révisé' }
    ];

    const CONFLICT_MESSAGE = 'Cette observation a été modifiée par un autre utilisateur. Rechargez la classe puis recommencez.';

    function todayIso() {
        const d = new Date();
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        return `${d.getFullYear()}-${month}-${day}`;
    }

    /**
     * Trie du plus récent au plus ancien. Une observation sans date (valide pour le suivi de mémorisation) passe en
     * dernier ; à date égale on garde l'ordre d'arrivée (tri stable).
     */
    function sortByDateDesc(entries) {
        return [...(entries || [])]
            .map((entry, index) => ({ entry, index }))
            .sort((a, b) => {
                const da = a.entry.evaluationDate || '';
                const db = b.entry.evaluationDate || '';
                if (da === db) return a.index - b.index;
                if (!da) return 1;
                if (!db) return -1;
                return da < db ? 1 : -1;
            })
            .map((x) => x.entry);
    }

    function integerInRange(value, min, max) {
        const n = Number(value);
        return value !== '' && value !== null && value !== undefined && Number.isInteger(n) && n >= min && n <= max;
    }

    function nonNegative(value) {
        const n = Number(value);
        return value !== '' && value !== null && value !== undefined && Number.isFinite(n) && n >= 0;
    }

    Alpine.data('coranPage', () => ({
        canEnterQuran: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant',

        classrooms: [],
        selectedClassroomId: '',
        activeTab: 'progress',
        statusOptions: STATUS_OPTIONS,

        isLoadingContext: true,
        isLoadingRows: false,
        // Les deux lectures sont indépendantes : l'échec de l'une n'efface pas l'autre onglet.
        progressRows: [],
        evaluationRows: [],
        progressError: null,
        evaluationsError: null,

        expanded: {},

        form: {
            open: false, mode: 'create', kind: 'progress', student: null, entry: null,
            juzNumber: '', hizbNumber: '', surahNumber: '', status: 'InProcess', evaluationDate: '', notes: '',
            memoryMistakes: 0, tajwidMistakes: 0, hesitations: 0, finalScore: '',
            errors: {}, saving: false
        },

        async init() {
            this.isLoadingContext = true;
            try {
                this.classrooms = await window.api.get('/classrooms');
            } catch (err) {
                this.progressError = window.api.toMessage(err, 'Impossible de charger les classes.');
            } finally {
                this.isLoadingContext = false;
            }
        },

        get hasSelection() {
            return Boolean(this.selectedClassroomId);
        },

        get rows() {
            return this.activeTab === 'progress' ? this.progressRows : this.evaluationRows;
        },

        get activeError() {
            return this.activeTab === 'progress' ? this.progressError : this.evaluationsError;
        },

        selectTab(tab) {
            this.activeTab = tab;
        },

        async loadClassroom(keepExpanded = false) {
            if (!keepExpanded) this.expanded = {};
            this.progressRows = [];
            this.evaluationRows = [];
            this.progressError = null;
            this.evaluationsError = null;
            if (!this.selectedClassroomId) return;

            this.isLoadingRows = true;
            const id = this.selectedClassroomId;
            const [progress, evaluations] = await Promise.allSettled([
                window.api.get(`/quran/progress/classroom/${id}`),
                window.api.get(`/quran/evaluations/classroom/${id}`)
            ]);
            // Une réponse tardive d'une classe abandonnée ne doit pas écraser la classe courante.
            if (id !== this.selectedClassroomId) return;

            if (progress.status === 'fulfilled') this.progressRows = progress.value;
            else this.progressError = window.api.toMessage(progress.reason, 'Impossible de charger le suivi de mémorisation.');

            if (evaluations.status === 'fulfilled') this.evaluationRows = evaluations.value;
            else this.evaluationsError = window.api.toMessage(evaluations.reason, 'Impossible de charger les évaluations orales.');

            this.isLoadingRows = false;
        },

        // ------------------------------------------------------------------ Affichage
        sortedEntries(row) {
            return sortByDateDesc(row.entries);
        },

        latestEntry(row) {
            return this.sortedEntries(row)[0] || null;
        },

        statusLabel(status) {
            return STATUS_LABELS[status] || status;
        },

        summary(row) {
            const last = this.latestEntry(row);
            if (!last) return 'Aucune observation';
            if (this.activeTab === 'progress') {
                return `Juz ${last.juzNumber} · Hizb ${last.hizbNumber} · Sourate ${last.surahNumber} — ${this.statusLabel(last.status)}`;
            }
            return `${this.formatDate(last.evaluationDate)} — note ${last.finalScore}`;
        },

        formatDate(value) {
            if (!value) return 'Sans date';
            const d = new Date(`${value}T00:00:00`);
            return Number.isNaN(d.getTime())
                ? value
                : d.toLocaleDateString('fr-FR', { day: '2-digit', month: 'long', year: 'numeric' });
        },

        isExpanded(row) {
            return Boolean(this.expanded[`${this.activeTab}:${row.studentId}`]);
        },

        toggle(row) {
            const key = `${this.activeTab}:${row.studentId}`;
            this.expanded = { ...this.expanded, [key]: !this.expanded[key] };
        },

        // ------------------------------------------------------------------ Modale
        resetForm(kind, student, entry) {
            const isProgress = kind === 'progress';
            this.form = {
                open: true,
                mode: entry ? 'edit' : 'create',
                kind,
                student,
                entry,
                juzNumber: entry && isProgress ? entry.juzNumber : '',
                hizbNumber: entry && isProgress ? entry.hizbNumber : '',
                surahNumber: entry && isProgress ? entry.surahNumber : '',
                status: entry && isProgress ? entry.status : 'InProcess',
                // Évaluation : aujourd'hui par défaut ; suivi : vide (la date est facultative).
                evaluationDate: entry ? (entry.evaluationDate || '') : (isProgress ? '' : todayIso()),
                notes: entry && isProgress ? (entry.notes || '') : '',
                memoryMistakes: entry && !isProgress ? entry.memoryMistakes : 0,
                tajwidMistakes: entry && !isProgress ? entry.tajwidMistakes : 0,
                hesitations: entry && !isProgress ? entry.hesitations : 0,
                finalScore: entry && !isProgress ? entry.finalScore : '',
                errors: {},
                saving: false
            };
        },

        openCreate(row) {
            if (!this.canEnterQuran) return;
            this.resetForm(this.activeTab === 'progress' ? 'progress' : 'evaluation', row, null);
        },

        openEdit(row, entry) {
            if (!this.canEnterQuran) return;
            this.resetForm(this.activeTab === 'progress' ? 'progress' : 'evaluation', row, entry);
        },

        closeForm() {
            if (this.form.saving) return;
            this.form.open = false;
        },

        get isEdit() {
            return this.form.mode === 'edit';
        },

        /** Bornes du serveur (CreateQuranProgress/EvaluationCommandValidator) — confort de saisie seulement. */
        validate() {
            const f = this.form;
            const errors = {};
            if (f.kind === 'progress') {
                if (!this.isEdit) {
                    if (!integerInRange(f.juzNumber, 1, 30)) errors.juzNumber = 'Juz : un nombre de 1 à 30.';
                    if (!integerInRange(f.hizbNumber, 1, 60)) errors.hizbNumber = 'Hizb : un nombre de 1 à 60.';
                    if (!integerInRange(f.surahNumber, 1, 114)) errors.surahNumber = 'Sourate : un nombre de 1 à 114.';
                }
                if (!f.status) errors.status = 'Choisissez un statut.';
                if (f.notes && f.notes.length > 2000) errors.notes = 'Notes : 2 000 caractères au plus.';
            } else {
                if (!f.evaluationDate) errors.evaluationDate = 'La date de l\'évaluation est obligatoire.';
                if (!nonNegative(f.memoryMistakes)) errors.memoryMistakes = 'Un nombre positif ou nul.';
                if (!nonNegative(f.tajwidMistakes)) errors.tajwidMistakes = 'Un nombre positif ou nul.';
                if (!nonNegative(f.hesitations)) errors.hesitations = 'Un nombre positif ou nul.';
                if (!nonNegative(f.finalScore)) errors.finalScore = 'Une note ne peut pas être négative.';
            }
            return errors;
        },

        /**
         * Charge utile envoyée au serveur. En correction, les champs d'identité (élève, Juz, Hizb, Sourate) ne sont JAMAIS
         * renvoyés : ils sont immuables côté serveur (décision #8 de la spec CQRS), le client ne peut donc pas les altérer.
         */
        buildPayload() {
            const f = this.form;
            if (f.kind === 'progress') {
                const base = { status: f.status, evaluationDate: f.evaluationDate || null, notes: f.notes ? f.notes : null };
                return this.isEdit
                    ? { ...base, rowVersion: f.entry.rowVersion }
                    : {
                        studentId: f.student.studentId,
                        juzNumber: Number(f.juzNumber), hizbNumber: Number(f.hizbNumber), surahNumber: Number(f.surahNumber),
                        ...base
                    };
            }
            const base = {
                evaluationDate: f.evaluationDate,
                memoryMistakes: Number(f.memoryMistakes), tajwidMistakes: Number(f.tajwidMistakes),
                hesitations: Number(f.hesitations), finalScore: Number(f.finalScore)
            };
            return this.isEdit
                ? { ...base, rowVersion: f.entry.rowVersion }
                : { studentId: f.student.studentId, ...base };
        },

        async submit() {
            if (this.form.saving) return;
            const errors = this.validate();
            this.form.errors = errors;
            if (Object.keys(errors).length > 0) return;

            this.form.saving = true;
            const resource = this.form.kind === 'progress' ? 'progress' : 'evaluations';
            try {
                if (this.isEdit) {
                    await window.api.put(`/quran/${resource}/${this.form.entry.id}`, this.buildPayload());
                } else {
                    await window.api.post(`/quran/${resource}`, this.buildPayload());
                }
                this.form.saving = false;
                this.form.open = false;
                window.toast?.success(this.isEdit ? 'Observation corrigée.' : 'Observation enregistrée.');
                await this.loadClassroom(true);
            } catch (err) {
                this.form.saving = false;
                this.form.errors = { global: this.messageFor(err) };
            }
        },

        /** 409 → message dédié (même formulation que la garde de concurrence de l'Internat) ; sinon le message du serveur. */
        messageFor(err) {
            if (err && err.status === 409) return CONFLICT_MESSAGE;
            return window.api.toMessage(err, 'Enregistrement impossible.');
        }
    }));
});
