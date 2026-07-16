/**
 * Écran de saisie des notes — tickets JGK-G01/G02.
 *
 * Trois sélecteurs (classe, matière, trimestre de l'année active) chargent une grille : une ligne par
 * élève, une colonne Devoir et une colonne Composition (GET /grades). Chaque cellule s'enregistre
 * seule à la perte de focus — POST /grades pour une note qui n'existe pas encore, PUT /grades/{id}
 * pour la corriger — sans bouton « Enregistrer » global : sur une classe de quarante élèves, un
 * enregistrement groupé forcerait à tout ressaisir après une seule erreur de saisie.
 *
 * SAISIR (POST) est réservé à l'Enseignant, CORRIGER (PUT) est ouvert au Directeur et à l'Enseignant
 * (docs/Volume_7_Security.md « Notes », comme GradesController) : un Directeur ne voit donc pas de
 * champ actif sur une cellule encore vide, seulement sur celles déjà notées.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('gradesView', () => ({
        canEnterGrades: window.auth.role === 'Enseignant',

        classrooms: [],
        subjects: [],
        terms: [],
        gradingScale: 20,

        selectedClassroomId: '',
        selectedSubjectId: '',
        selectedTermId: '',

        isLoadingContext: true,
        isLoadingRows: false,
        rows: [],
        conflictError: false,

        get hasSelection() {
            return Boolean(this.selectedClassroomId && this.selectedSubjectId && this.selectedTermId);
        },

        async init() {
            this.isLoadingContext = true;
            try {
                const [classrooms, subjects, years, settings] = await Promise.all([
                    window.api.get('/classrooms'),
                    window.api.get('/subjects'),
                    window.api.get('/school-years'),
                    window.api.get('/schools/current/settings')
                ]);
                this.classrooms = classrooms;
                this.subjects = subjects;
                this.gradingScale = Number(settings.gradingScale) || 20;

                // Seule l'année ACTIVE propose des trimestres à noter : les années passées sont en
                // lecture seule (docs/Volume_1_Cahier_des_Charges.md, ticket JGK-C01).
                const activeYear = years.find(y => y.isActive);
                if (activeYear) {
                    this.terms = await window.api.get(`/school-years/${activeYear.id}/terms`);
                }
            } finally {
                this.isLoadingContext = false;
            }
        },

        async reload() {
            this.conflictError = false;

            if (!this.hasSelection) {
                this.rows = [];
                return;
            }

            this.isLoadingRows = true;
            try {
                const data = await window.api.get(
                    `/grades?classroomId=${this.selectedClassroomId}&subjectId=${this.selectedSubjectId}&termId=${this.selectedTermId}`);

                this.rows = data.map(r => ({
                    studentId: r.studentId,
                    matricule: r.matricule,
                    fullName: r.fullName,
                    devoir: this.toCell(r.devoir),
                    composition: this.toCell(r.composition)
                }));
            } finally {
                this.isLoadingRows = false;
            }
        },

        toCell(cell) {
            return {
                id: cell ? cell.id : null,
                rowVersion: cell ? cell.rowVersion : null,
                value: cell ? cell.value : '',
                original: cell ? cell.value : '',
                status: 'idle',
                error: null
            };
        },

        /** POST/PUT partagent la casse EvaluationType du domaine (SamaEcole.Domain.Enums.EvaluationType). */
        evaluationTypeFor(field) {
            return field === 'devoir' ? 'Devoir' : 'Composition';
        },

        async saveCell(rowIndex, field) {
            const row = this.rows[rowIndex];
            const cell = row[field];

            // Champ laissé vide : rien à enregistrer, ce n'est pas une note à zéro.
            if (cell.value === '' || cell.value === null || cell.value === undefined) {
                cell.error = null;
                return;
            }

            const value = Number(cell.value);
            if (Number.isNaN(value) || value === cell.original) return;

            cell.status = 'saving';
            cell.error = null;

            try {
                let result;
                if (cell.id) {
                    result = await window.api.put(`/grades/${cell.id}`, { value, rowVersion: cell.rowVersion });
                } else {
                    result = await window.api.post('/grades', {
                        studentId: row.studentId,
                        subjectId: this.selectedSubjectId,
                        termId: this.selectedTermId,
                        evaluationType: this.evaluationTypeFor(field),
                        value
                    });
                }

                cell.id = result.id;
                cell.rowVersion = result.rowVersion;
                cell.value = result.value;
                cell.original = result.value;
                cell.status = 'saved';
                setTimeout(() => { if (cell.status === 'saved') cell.status = 'idle'; }, 1500);
            } catch (err) {
                if (err.status === 409) {
                    // Verrou optimiste (AGENTS.md règle #5) : jamais un écrasement silencieux, on force
                    // un rechargement complet de la grille avant toute nouvelle saisie.
                    this.conflictError = true;
                    cell.status = 'idle';
                    cell.value = cell.original;
                } else {
                    cell.status = 'error';
                    const fieldErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement.");
                    cell.error = fieldErrors.value || fieldErrors.global;
                }
            }
        },

        /** Enter descend dans la même colonne, comme un tableur — la tabulation navigue déjà naturellement d'un champ à l'autre. */
        focusNextRow(rowIndex, field) {
            const next = document.querySelector(`input[data-row="${rowIndex + 1}"][data-field="${field}"]`);
            if (next) next.focus();
        }
    }));
});
