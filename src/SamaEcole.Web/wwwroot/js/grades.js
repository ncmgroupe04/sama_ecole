/**
 * Écran de saisie des notes — tickets JGK-G01/G02.
 *
 * Trois sélecteurs (classe, matière, trimestre de l'année active) chargent une grille : une ligne par
 * élève, une colonne Devoir 1, une colonne Devoir 2 et une colonne Composition (GET /grades). Chaque
 * cellule s'enregistre seule à la perte de focus — POST /grades pour une note qui n'existe pas encore,
 * PUT /grades/{id} pour la corriger — sans bouton « Enregistrer » global : sur une classe de quarante
 * élèves, un enregistrement groupé forcerait à tout ressaisir après une seule erreur de saisie.
 *
 * Droits (GradesController, Évolution N°1) : SAISIR une note (POST) est ouvert au Directeur, au
 * Secrétariat et à l'Enseignant (canEnterGrades). CORRIGER une note déjà enregistrée (PUT) est ouvert
 * aux trois, mais l'Enseignant est borné côté serveur — fenêtre de correction de l'école, ET auteur de
 * la note ou affecté à la classe/matière. Le serveur dit cellule par cellule si la correction est
 * possible (champ canEdit) : l'écran grise la cellule sur cette base, sans recalculer une règle qui
 * exige l'heure serveur et l'affectation de l'enseignant. Confort d'affichage — la vraie garde reste
 * PUT /grades/{id} (403). ANNULER (DELETE) reste réservé au Directeur et au Secrétariat.
 *
 * Import Excel : mode de saisie ALTERNATIF, ouvert aux mêmes rôles que la saisie — une feuille large (une
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
        // openPdfPreview (ouvre le PDF dans un nouvel onglet, visionneuse native du navigateur).
        ...window.pdfPreview.state(),

        canEnterGrades: window.auth.role === 'Enseignant' || window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',
        isTeacher: window.auth.role === 'Enseignant',

        // Infobulle d'une cellule dont la correction est verrouillée (canEdit faux, voir toCell).
        lockedHint: "Correction verrouillée : le délai est dépassé, ou cette note n'est ni la vôtre ni celle d'une matière et d'une classe qui vous sont affectées. Demandez au Directeur ou au Secrétariat.",

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

        // Fiche de saisie PAPIER (PDF vierge, GET /grades/sheet/print) : une évaluation à la fois, choisie ici.
        sheetEvaluation: 'Devoir1',
        printingSheet: false,

        downloadingClassBulletins: false,
        downloadingClassDeliberation: false,
        // Conseil de classe (Évolution N°7) : PV annuel et application des propositions de décision.
        activeYearId: null,
        applyingDecisions: false,
        councilNotice: null,
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
                this.activeYearId = activeYear ? activeYear.id : null;
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
                // Une cellule vide se saisit toujours (POST) ; une note existante se corrige seulement si le
                // serveur l'autorise (fenêtre de correction, auteur ou affectation — voir GradeEditPolicy).
                canEdit: cell ? cell.canEdit : true,
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

        /**
         * Fiche de saisie papier : une grille VIERGE (élèves par ordre alphabétique, cases Note et
         * Appréciation vides) à imprimer et remplir au stylo dans la salle, avant de reporter les notes
         * à l'écran. Aperçu dans la modale partagée — l'enseignant l'imprime depuis l'en-tête. Une seule
         * évaluation par fiche (choix `sheetEvaluation`), jamais un « Devoir 1 » par défaut silencieux :
         * le type est toujours envoyé explicitement.
         */
        async printPaperSheet() {
            if (!this.hasSelection) return;
            this.classBulletinsError = null;
            this.printingSheet = true;
            try {
                const className = this.classNameFor(this.selectedClassroomId);
                const label = { Devoir1: 'Devoir 1', Devoir2: 'Devoir 2', Composition: 'Composition' }[this.sheetEvaluation];
                await this.openPdfPreview(
                    `/api/v1/grades/sheet/print?classroomId=${this.selectedClassroomId}&subjectId=${this.selectedSubjectId}&termId=${this.selectedTermId}&evaluationType=${this.sheetEvaluation}`,
                    `Fiche de saisie — ${className} — ${label}`,
                    `Fiche_Notes_${className}_${label.replace(' ', '')}.pdf`
                );
            } catch (err) {
                this.classBulletinsError = (err && err.message) || 'Génération de la fiche de saisie impossible.';
            } finally {
                this.printingSheet = false;
            }
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
         * Aperçu des bulletins fusionnés (une page A5 par élève), ouvert dans un nouvel onglet : le
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

        /**
         * PV de délibération : aperçu dans la modale partagée (comme previewClassBulletinsMergedPdf) —
         * le conseil de classe le relit avant impression ou téléchargement, tout depuis l'en-tête.
         */
        async downloadClassDeliberationPdf() {
            if (!this.hasClassAndTerm) return;
            this.classBulletinsError = null;
            this.downloadingClassDeliberation = true;
            try {
                const className = this.classNameFor(this.selectedClassroomId);
                await this.openPdfPreview(
                    `/api/v1/report-cards/class-deliberation/pdf?classroomId=${this.selectedClassroomId}&termId=${this.selectedTermId}`,
                    `PV de délibération — ${className}`,
                    `PV_Deliberation_${className}.pdf`
                );
            } catch (err) {
                this.classBulletinsError = (err && err.message) || 'Aperçu du PV de délibération impossible.';
            } finally {
                this.downloadingClassDeliberation = false;
            }
        },

        /** PV ANNUEL du conseil de classe (année active) : moyennes annuelles, décisions saisies ou proposées. */
        async downloadAnnualDeliberationPdf() {
            if (!this.selectedClassroomId || !this.activeYearId) return;
            this.classBulletinsError = null;
            this.downloadingClassDeliberation = true;
            try {
                const className = this.classNameFor(this.selectedClassroomId);
                await this.openPdfPreview(
                    `/api/v1/report-cards/class-deliberation/annual/pdf?classroomId=${this.selectedClassroomId}&schoolYearId=${this.activeYearId}`,
                    `PV annuel — ${className}`,
                    `PV_Annuel_${className}.pdf`
                );
            } catch (err) {
                this.classBulletinsError = (err && err.message) || 'Aperçu du PV annuel impossible.';
            } finally {
                this.downloadingClassDeliberation = false;
            }
        },

        /** Directeur et Secrétariat : enregistre les décisions proposées pour les élèves sans décision (jamais d'écrasement). */
        get canApplyDecisions() {
            return ['Directeur', 'Secretariat'].includes(window.auth.role);
        },

        async applyDecisionProposals() {
            if (!this.hasClassAndTerm || !this.canApplyDecisions) return;
            this.classBulletinsError = null;
            this.councilNotice = null;
            this.applyingDecisions = true;
            try {
                const result = await window.api.post('/report-cards/council-decisions/apply-proposals', {
                    classroomId: this.selectedClassroomId, termId: this.selectedTermId
                });
                this.councilNotice = `${result.applied} décision(s) enregistrée(s), ${result.alreadyDecided} déjà prise(s) conservée(s), `
                    + `${result.withoutAverage} élève(s) sans moyenne annuelle.`;
            } catch (err) {
                this.classBulletinsError = window.api.toMessage(err, "Erreur lors de l'application des propositions.");
            } finally {
                this.applyingDecisions = false;
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
