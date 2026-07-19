/**
 * Écran de saisie des notes — tickets JGK-G01/G02.
 *
 * Trois sélecteurs (classe, matière, trimestre de l'année active) chargent une grille : une ligne par
 * élève, une colonne Devoir et une colonne Composition (GET /grades). Chaque cellule s'enregistre
 * seule à la perte de focus — POST /grades pour une note qui n'existe pas encore, PUT /grades/{id}
 * pour la corriger — sans bouton « Enregistrer » global : sur une classe de quarante élèves, un
 * enregistrement groupé forcerait à tout ressaisir après une seule erreur de saisie.
 *
 * Matrice d'autorisation "Photoshop" — contrôle strict et NON révocable (GradesController) : SAISIR
 * (POST) une note qui n'existe pas encore reste réservé à l'Enseignant (canEnterGrades) ; CORRIGER
 * (PUT) ou ANNULER (DELETE) une note déjà enregistrée est réservé au Directeur et au Secrétariat
 * (canCorrectGrades) — l'Enseignant en perd le droit dès l'enregistrement initial, même sur sa propre
 * saisie. D'où deux conditions distinctes par cellule : vide → gouvernée par canEnterGrades ;
 * déjà notée → gouvernée par canCorrectGrades.
 *
 * Import CSV/Excel : mode de saisie ALTERNATIF, réservé lui aussi à l'Enseignant — un fichier à deux
 * colonnes (matricule, note) remplit en une fois UNE colonne (Devoir OU Composition) pour toute la
 * classe (POST /grades/import). Tout le fichier est validé côté serveur avant la moindre écriture :
 * soit il est intégralement accepté, soit rien n'est enregistré et chaque ligne en erreur est listée.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('gradesView', () => ({
        canEnterGrades: window.auth.role === 'Enseignant',
        canCorrectGrades: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // Télécharger les bulletins de la classe (ZIP ou PDF fusionné) est réservé au Directeur/Enseignant
        // côté serveur (ReportCardsController, ticket JGK-G03) — le Secrétariat n'y a pas accès, comme
        // pour le bulletin individuel (voir students.js, canViewReportCard).
        canViewReportCard: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant',

        classrooms: [],
        subjects: [],
        terms: [],
        gradingScale: 20,
        // Consommé par le partiel _ErrorBanner partagé (x-show="error") : jamais renseigné en
        // pratique aujourd'hui (chaque échec réseau a déjà son propre traitement ci-dessous), mais
        // sans cette déclaration Alpine évalue "error" comme une référence indéfinie à chaque rendu.
        error: null,

        downloadingClassBulletins: false,
        classBulletinsError: null,

        selectedClassroomId: '',
        selectedSubjectId: '',
        selectedTermId: '',

        isLoadingContext: true,
        isLoadingRows: false,
        rows: [],
        conflictError: false,

        // Import par fichier CSV/Excel.
        isImportOpen: false,
        importEvaluationType: '', // 'devoir' | 'composition' — mêmes clés que data-field sur la grille
        importFile: null,
        importSubmitting: false,
        importErrors: [],
        importGlobalError: null,
        importSuccess: null,

        get hasSelection() {
            return Boolean(this.selectedClassroomId && this.selectedSubjectId && this.selectedTermId);
        },

        // Les bulletins couvrent TOUTES les matières de l'élève : classe + trimestre suffisent, la
        // matière du sélecteur de saisie n'entre pas en jeu ici.
        get hasClassAndTerm() {
            return Boolean(this.selectedClassroomId && this.selectedTermId);
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
                setTimeout(() => { if (cell.status === 'saved') cell.status = 'idle'; }, 2000);
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
        },

        // ------------------------------------------------------------ Bulletins de classe (JGK-G03)

        /** Fetch bas niveau partagé par le ZIP et le PDF fusionné : même mécanique que downloadReportCard (students.js). */
        async fetchClassBulletins(endpoint) {
            if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                await window.api.refreshOrRedirect();
            }

            const response = await fetch(
                `/api/v1/report-cards/class-bulletins/${endpoint}?classroomId=${this.selectedClassroomId}&termId=${this.selectedTermId}`,
                {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });

            if (!response.ok) throw await window.api.toError(response);

            return response.blob();
        },

        triggerDownload(blob, fileName) {
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        },

        classNameFor(classroomId) {
            const classroom = this.classrooms.find(c => c.id === classroomId);
            return classroom ? classroom.name : 'classe';
        },

        async downloadClassBulletinsZip() {
            if (!this.hasClassAndTerm) return;
            this.classBulletinsError = null;
            this.downloadingClassBulletins = true;
            try {
                const blob = await this.fetchClassBulletins('zip');
                this.triggerDownload(blob, `Bulletins_${this.classNameFor(this.selectedClassroomId)}.zip`);
            } catch (err) {
                this.classBulletinsError = (err && err.message) || 'Téléchargement des bulletins impossible.';
            } finally {
                this.downloadingClassBulletins = false;
            }
        },

        async downloadClassBulletinsMergedPdf() {
            if (!this.hasClassAndTerm) return;
            this.classBulletinsError = null;
            this.downloadingClassBulletins = true;
            try {
                const blob = await this.fetchClassBulletins('merged-pdf');
                this.triggerDownload(blob, `Bulletins_${this.classNameFor(this.selectedClassroomId)}.pdf`);
            } catch (err) {
                this.classBulletinsError = (err && err.message) || 'Téléchargement des bulletins impossible.';
            } finally {
                this.downloadingClassBulletins = false;
            }
        },

        openImport() {
            this.importEvaluationType = '';
            this.importFile = null;
            this.importErrors = [];
            this.importGlobalError = null;
            this.importSuccess = null;
            this.isImportOpen = true;
        },

        onImportFileChange(event) {
            this.importFile = event.target.files[0] || null;
        },

        /**
         * Modèle pré-rempli avec les matricules EXACTS de la classe déjà chargée (this.rows) : la
         * première source d'erreur de matricule est un matricule retapé à la main, ce modèle l'élimine
         * d'office. Généré côté client, sans aller-retour serveur : les données sont déjà sous les yeux.
         */
        downloadTemplate() {
            if (!this.importEvaluationType) return;

            const lines = ['Matricule;Note'];
            for (const row of this.rows) {
                const cell = row[this.importEvaluationType];
                lines.push(`${row.matricule};${cell && cell.value !== '' ? cell.value : ''}`);
            }

            // BOM UTF-8 : ouverture propre dans Excel FR (même convention que AttendanceReportCsv côté serveur).
            const bom = String.fromCharCode(0xFEFF);
            const blob = new Blob([bom + lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = `modele-notes-${this.importEvaluationType}.csv`;
            link.click();
            URL.revokeObjectURL(url);
        },

        async submitImport() {
            this.importGlobalError = null;
            this.importErrors = [];
            this.importSuccess = null;

            if (!this.importEvaluationType) {
                this.importGlobalError = "Choisissez d'abord la colonne à remplir : Devoir ou Composition.";
                return;
            }
            if (!this.importFile) {
                this.importGlobalError = 'Choisissez un fichier à importer.';
                return;
            }

            this.importSubmitting = true;
            try {
                const formData = new FormData();
                formData.append('classroomId', this.selectedClassroomId);
                formData.append('subjectId', this.selectedSubjectId);
                formData.append('termId', this.selectedTermId);
                formData.append('evaluationType', this.evaluationTypeFor(this.importEvaluationType));
                formData.append('file', this.importFile);

                const result = await window.api.upload('/grades/import', formData);

                const parts = [];
                if (result.created) parts.push(`${result.created} créée${result.created > 1 ? 's' : ''}`);
                if (result.updated) parts.push(`${result.updated} corrigée${result.updated > 1 ? 's' : ''}`);
                if (result.unchanged) parts.push(`${result.unchanged} déjà à jour`);
                this.importSuccess = parts.length > 0 ? `Import réussi : ${parts.join(', ')}.` : 'Import réussi.';
                this.importFile = null;

                await this.reload();
            } catch (err) {
                this.applyImportError(err);
            } finally {
                this.importSubmitting = false;
            }
        },

        /**
         * `details` du serveur est { "Ligne 3": ["message"], "File": ["message"] } (ValidationException,
         * docs/Volume_4_API_Design.md §0.4) : "File" est une erreur de STRUCTURE (fichier vide, format
         * non supporté...), affichée à part des erreurs ligne par ligne.
         */
        applyImportError(err) {
            const details = err && err.details;
            if (!details || typeof details !== 'object') {
                this.importGlobalError = (err && err.message) || "Erreur lors de l'import.";
                return;
            }

            const firstMessage = (messages) => (Array.isArray(messages) ? messages[0] : String(messages));
            const entries = Object.entries(details);
            const fileEntry = entries.find(([key]) => key === 'File');

            if (fileEntry) this.importGlobalError = firstMessage(fileEntry[1]);

            this.importErrors = entries
                .filter(([key]) => key !== 'File')
                .map(([line, messages]) => ({ line, message: firstMessage(messages) }));

            if (!fileEntry && this.importErrors.length === 0) {
                this.importGlobalError = (err && err.message) || "Erreur lors de l'import.";
            }
        }
    }));
});
