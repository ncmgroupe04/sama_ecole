/**
 * Écran de saisie des notes — tickets JGK-G01/G02.
 *
 * Trois sélecteurs (classe, matière, trimestre de l'année active) chargent une grille : une ligne par
 * élève, une colonne Devoir 1, une colonne Devoir 2 et une colonne Composition (GET /grades). Chaque
 * cellule s'enregistre seule à la perte de focus — POST /grades pour une note qui n'existe pas encore,
 * PUT /grades/{id} pour la corriger — sans bouton « Enregistrer » global : sur une classe de quarante
 * élèves, un enregistrement groupé forcerait à tout ressaisir après une seule erreur de saisie.
 *
 * Matrice d'autorisation "Photoshop" — contrôle strict et NON révocable (GradesController) : SAISIR
 * (POST) une note qui n'existe pas encore reste réservé à l'Enseignant (canEnterGrades) ; CORRIGER
 * (PUT) ou ANNULER (DELETE) une note déjà enregistrée est réservé au Directeur et au Secrétariat
 * (canCorrectGrades) — l'Enseignant en perd le droit dès l'enregistrement initial, même sur sa propre
 * saisie. D'où deux conditions distinctes par cellule : vide → gouvernée par canEnterGrades ;
 * déjà notée → gouvernée par canCorrectGrades.
 *
 * Import Excel : mode de saisie ALTERNATIF, réservé lui aussi à l'Enseignant — une feuille large (une
 * ligne par élève, une colonne par épreuve : Matricule, Devoir 1, Devoir 2, Composition, dans un ordre
 * libre) remplit les trois colonnes de la classe en une fois (POST /grades/sheet/import). L'élève est
 * identifié par son matricule, l'épreuve par le NOM de sa colonne — jamais par la position — pour
 * exclure toute inversion. Un aperçu (dryRun) précède toujours l'écriture : tout le fichier est validé
 * côté serveur avant la moindre écriture, soit il est intégralement accepté, soit rien n'est enregistré
 * et chaque ligne en erreur est listée.
 */

// Miroir client de CycleTypeExtensions.UsesSimplifiedGrading (SamaEcole.Domain) : les cycles notés
// sur /10. Toute évolution de l'énumération côté serveur doit être répercutée ici — c'est le seul
// endroit du front qui décide du barème affiché.
const SIMPLIFIED_GRADING_CYCLES = ['Primaire', 'Maternelle'];

document.addEventListener('alpine:init', () => {
    Alpine.data('gradesView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : previewClassBulletinsMergedPdf() appelle
        // openPdfPreview ; la vue monte la partial _PdfPreviewModal.
        ...window.pdfPreview.state(),

        canEnterGrades: window.auth.role === 'Enseignant' || window.auth.role === 'Directeur',
        canCorrectGrades: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // Télécharger les bulletins de la classe (ZIP ou PDF fusionné) est ouvert au Directeur, à
        // l'Enseignant ET au Secrétariat côté serveur (ReportCardsController.ReportCardDownloadRoles,
        // ticket JGK-G03) — même portée que le bulletin individuel (voir students.js, canDownloadReportCard).
        canDownloadReportCard: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant' || window.auth.role === 'Secretariat',

        classrooms: [],
        subjects: [],
        terms: [],
        // Consommé par le partiel _ErrorBanner partagé (x-show="error") : jamais renseigné en
        // pratique aujourd'hui (chaque échec réseau a déjà son propre traitement ci-dessous), mais
        // sans cette déclaration Alpine évalue "error" comme une référence indéfinie à chaque rendu.
        error: null,

        downloadingClassBulletins: false,
        downloadingClassDeliberation: false,
        classBulletinsError: null,

        selectedClassroomId: '',
        selectedSubjectId: '',
        selectedTermId: '',

        isLoadingContext: true,
        isLoadingRows: false,
        rows: [],
        conflictError: false,

        // Import par fichier Excel.
        isImportOpen: false,
        importFile: null,
        importSubmitting: false,
        importErrors: [],
        importGlobalError: null,
        importSuccess: null,
        // Aperçu (dryRun) : rempli après une vérification réussie, tant que rien n'a encore été écrit.
        importPreview: null,
        importPreviewSummary: '',
        downloadingGradeSheet: false,

        get hasSelection() {
            return Boolean(this.selectedClassroomId && this.selectedSubjectId && this.selectedTermId);
        },

        // Les bulletins couvrent TOUTES les matières de l'élève : classe + trimestre suffisent, la
        // matière du sélecteur de saisie n'entre pas en jeu ici.
        get hasClassAndTerm() {
            return Boolean(this.selectedClassroomId && this.selectedTermId);
        },

        // Barème de saisie = celui du CYCLE de la classe sélectionnée, PAS un réglage global d'école :
        // Maternelle & Primaire /10, Collège & Lycée /20 (système hybride). Même règle que côté serveur
        // (CycleTypeExtensions.UsesSimplifiedGrading, appliquée par GradingScaleGuard) — le serveur
        // reste l'autorité, ceci n'est qu'un garde-fou de saisie (attribut max) et un repère visuel.
        // La liste DOIT rester alignée sur UsesSimplifiedGrading : n'y voir que « Primaire » affichait
        // /20 en Maternelle, puis faisait rejeter la saisie par un 422 que rien n'annonçait à l'écran.
        /**
         * Plafond de saisie : celui de la MATIÈRE sélectionnée quand l'école lui en a fixé un
         * (grilles par compétences du primaire : /40, /60, /24, /16…), et à défaut celui du cycle de
         * la classe (Primaire /10, Collège & Lycée /20). Exactement la résolution du serveur
         * (GradeCalculator.EffectiveMaxScore) : plafonner à /20 une ligne notée sur 40 ferait refuser
         * ici, dans le navigateur, une note que l'API accepterait.
         */
        get gradingScale() {
            const subject = this.subjects.find(s => s.id === this.selectedSubjectId);
            if (subject && subject.maxScore) return Number(subject.maxScore);

            const classroom = this.classrooms.find(c => c.id === this.selectedClassroomId);
            return classroom && SIMPLIFIED_GRADING_CYCLES.includes(classroom.cycle) ? 10 : 20;
        },

        /**
         * Les matières NOTABLES : tout sauf les domaines d'une grille par compétences. Un domaine
         * (« Lang & Com. », « Français ») ne fait que regrouper des lignes sur le bulletin — la note
         * se saisit sur ses activités. Le serveur refuse d'ailleurs une note sur un domaine ; le
         * proposer ici n'offrirait qu'une impasse.
         */
        get gradableSubjects() {
            const domainIds = new Set(
                this.subjects.filter(s => s.parentSubjectId).map(s => s.parentSubjectId));
            return this.subjects.filter(s => !domainIds.has(s.id));
        },

        /** « Français › Ressources » : sans son domaine, « Ressources » apparaîtrait plusieurs fois
         *  à l'identique dans la liste, une par domaine qui en porte une. */
        subjectLabel(subject) {
            if (!subject.parentSubjectId) return subject.name;
            const domain = this.subjects.find(s => s.id === subject.parentSubjectId);
            return domain ? `${domain.name} › ${subject.name}` : subject.name;
        },

        async init() {
            this.isLoadingContext = true;
            try {
                const [classrooms, subjects, years] = await Promise.all([
                    window.api.get('/classrooms'),
                    window.api.get('/subjects'),
                    window.api.get('/school-years')
                ]);
                this.classrooms = classrooms;
                this.subjects = subjects;

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
                    devoir1: this.toCell(r.devoir1),
                    devoir2: this.toCell(r.devoir2),
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
            return { devoir1: 'Devoir1', devoir2: 'Devoir2', composition: 'Composition' }[field];
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
            if (Number.isNaN(value)) {
                // Saisie non numérique au blur : sans ce retour, la valeur invalide reste affichée
                // sans jamais être enregistrée — l'enseignant croit la note prise en compte jusqu'au
                // prochain rechargement de la grille, où elle disparaît silencieusement.
                cell.status = 'error';
                cell.error = 'Note invalide : entrez un nombre.';
                return;
            }
            if (value === cell.original) return;

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

        /** Fetch bas niveau des bulletins de classe (blob authentifié) — utilisé par le téléchargement ZIP. */
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

        /**
         * Aperçu des bulletins fusionnés (une page A5 par élève) dans la modale partagée : le
         * Directeur/Enseignant/Secrétariat les vérifie AVANT impression ou téléchargement, tout
         * depuis l'en-tête de la modale. Le ZIP et le PV restent en téléchargement direct (un ZIP
         * ne se prévisualise pas, et le PV est un document unique déjà court).
         */
        async previewClassBulletinsMergedPdf() {
            if (!this.hasClassAndTerm) return;
            this.classBulletinsError = null;
            this.downloadingClassBulletins = true;
            try {
                const className = this.classNameFor(this.selectedClassroomId);
                await this.openPdfPreview(
                    `/api/v1/report-cards/class-bulletins/merged-pdf?classroomId=${this.selectedClassroomId}&termId=${this.selectedTermId}`,
                    `Bulletins — ${className}`,
                    `Bulletins_${className}.pdf`
                );
            } catch (err) {
                this.classBulletinsError = (err && err.message) || 'Aperçu des bulletins impossible.';
            } finally {
                this.downloadingClassBulletins = false;
            }
        },

        async downloadClassDeliberationPdf() {
            if (!this.hasClassAndTerm) return;
            this.classBulletinsError = null;
            this.downloadingClassDeliberation = true;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(
                    `/api/v1/report-cards/class-deliberation/pdf?classroomId=${this.selectedClassroomId}&termId=${this.selectedTermId}`,
                    {
                        headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                        credentials: 'same-origin'
                    });

                if (!response.ok) throw await window.api.toError(response);

                const blob = await response.blob();
                this.triggerDownload(blob, `PV_Deliberation_${this.classNameFor(this.selectedClassroomId)}.pdf`);
            } catch (err) {
                this.classBulletinsError = (err && err.message) || 'Téléchargement du PV de délibération impossible.';
            } finally {
                this.downloadingClassDeliberation = false;
            }
        },

        openImport() {
            this.importFile = null;
            this.importErrors = [];
            this.importGlobalError = null;
            this.importSuccess = null;
            this.importPreview = null;
            this.importPreviewSummary = '';
            this.isImportOpen = true;
        },

        onImportFileChange(event) {
            this.importFile = event.target.files[0] || null;
            // Un nouveau fichier invalide l'aperçu précédent : il faut le revérifier avant d'écrire.
            this.importPreview = null;
        },

        /**
         * Feuille Excel générée côté serveur (GET /grades/sheet/export) : matricules, noms et notes
         * déjà saisies de la classe/matière/trimestre courants, colonnes Matricule et Nom & Prénom
         * verrouillées — même mécanique bas niveau que fetchClassBulletins (blob authentifié).
         */
        async downloadGradeSheet() {
            if (!this.selectedClassroomId || !this.selectedSubjectId || !this.selectedTermId) {
                console.error('Sélection classe/matière/trimestre invalide ou indéfinie', {
                    classroomId: this.selectedClassroomId, subjectId: this.selectedSubjectId, termId: this.selectedTermId
                });
                return;
            }
            this.downloadingGradeSheet = true;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(
                    `/api/v1/grades/sheet/export?classroomId=${this.selectedClassroomId}&subjectId=${this.selectedSubjectId}&termId=${this.selectedTermId}`,
                    {
                        headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                        credentials: 'same-origin'
                    });

                if (!response.ok) throw await window.api.toError(response);

                const blob = await response.blob();
                this.triggerDownload(blob, `Feuille-Notes-${this.classNameFor(this.selectedClassroomId)}.xlsx`);
            } catch (err) {
                this.importGlobalError = (err && err.message) || 'Téléchargement de la feuille impossible.';
            } finally {
                this.downloadingGradeSheet = false;
            }
        },

        /**
         * dryRun=true (« Vérifier ») : valide tout le fichier et affiche le résultat, RIEN n'est écrit —
         * l'enseignant confirme ensuite en connaissance de cause. dryRun=false (« Confirmer ») : même
         * validation, suivie de l'écriture en une transaction. Un nouveau fichier choisi après un
         * aperçu revient obligatoirement à l'étape de vérification (voir onImportFileChange).
         */
        async submitImport(dryRun) {
            this.importGlobalError = null;
            this.importErrors = [];
            this.importSuccess = null;

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
                formData.append('dryRun', dryRun);
                formData.append('file', this.importFile);

                const result = await window.api.upload('/grades/sheet/import', formData);

                const parts = [];
                if (result.created) parts.push(`${result.created} créée${result.created > 1 ? 's' : ''}`);
                if (result.updated) parts.push(`${result.updated} corrigée${result.updated > 1 ? 's' : ''}`);
                if (result.unchanged) parts.push(`${result.unchanged} déjà à jour`);
                const summary = parts.length > 0
                    ? `${result.studentsMatched} élève${result.studentsMatched > 1 ? 's' : ''} reconnu${result.studentsMatched > 1 ? 's' : ''} : ${parts.join(', ')}`
                    : `${result.studentsMatched} élève${result.studentsMatched > 1 ? 's' : ''} reconnu${result.studentsMatched > 1 ? 's' : ''}`;

                if (dryRun) {
                    this.importPreview = result;
                    this.importPreviewSummary = summary;
                } else {
                    this.importSuccess = `Import réussi : ${summary}.`;
                    this.importPreview = null;
                    this.importFile = null;
                    await this.reload();
                }
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
