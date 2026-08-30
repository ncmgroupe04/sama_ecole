/**
 * Forme vide stable de `editingStudent` / `changingEnrollmentStatus` (bug JGK-UI02-BIS, voir le
 * commentaire sur ces deux propriétés plus bas) : une fonction plutôt qu'une constante partagée,
 * pour que closeEditStudent()/closeChangeEnrollmentStatus() reçoivent un objet FRAIS à chaque
 * fermeture — jamais la même référence mutée par la modale précédente.
 */
function emptyEditingStudent() {
    return {
        fullName: '', birthDate: '', birthPlace: '', gender: '', classroomId: '',
        photoUrl: '', photoDisplayUrl: '', guardianName: '', guardianPhone: '', guardianEmail: '',
        address: '', rowVersion: null
    };
}

function emptyChangingEnrollmentStatus() {
    return { enrollmentId: null, schoolYearLabel: '', rowVersion: null, newStatus: 'DroppedOut' };
}

document.addEventListener('alpine:init', () => {
    Alpine.data('studentsView', () => ({
        students: [],
        classrooms: [],
        totalCount: 0,
        girlsCount: 0,
        boysCount: 0,
        newEnrollmentsCount: 0,
        page: 1,
        pageSize: 10,
        isLoading: false,
        error: null,

        // Filtres — recherche et classe sont envoyés au serveur (GetStudentsQuery les supporte déjà,
        // la pagination reste correcte) ; genre a été ajouté côté serveur pour rester cohérent (les
        // deux filtres visibles doivent réellement filtrer, pas seulement la page affichée).
        search: '',
        classroomFilter: '',
        genderFilter: '',

        // Portée par année scolaire. Par défaut 'active' : l'écran Élèves montre l'effectif INSCRIT pour
        // l'année active (le contexte de travail courant, cohérent avec les inscriptions, les notes et les
        // finances). 'all' rebascule sur l'annuaire complet — indispensable juste après un import ou une
        // création, tant que l'élève n'est pas encore (ré)inscrit. Traduit en `activeYearOnly` côté API
        // (GetStudentsQuery), qui reste par défaut l'annuaire complet.
        yearScope: 'active',


        // Fiche élève (JGK-D02) — detailStudent porte la ligne de liste (affichage immédiat de
        // l'identité), studentDetail la fiche complète chargée depuis GET /students/{id}
        // (historique scolaire, notes, paiements).
        detailStudent: null,
        studentDetail: null,
        isLoadingDetails: false,
        detailError: null,
        detailTab: 'history',

        // Identifiant National de l'Élève (module Intégration étatique, JGK-M01). Géré depuis la
        // fiche : soit on saisit le numéro OFFICIEL reçu de l'IEF, soit on demande un numéro
        // PROVISOIRE de secours (préfixé « P », sans valeur officielle). La garde réelle est
        // StateIntegrationController (Directeur/Secrétariat) ; le refus d'écraser un IEN officiel par
        // un provisoire est porté par le Handler.
        ienModal: { open: false, official: '', busy: false, provisioning: false, error: null },

        // Bulletin PDF (JGK-G03) : téléchargé depuis l'onglet Notes & bulletins, un trimestre à la
        // fois. Réservé au Directeur/Enseignant côté serveur (ReportCardsController) — même règle
        // qu'ici pour ne pas afficher un bouton qui répondrait 403.
        downloadingTermId: null,
        sendingTermId: null,
        reportCardError: null,

        // Observations du conseil (distinction + texte), imprimées sur le bulletin — mêmes rôles que
        // le téléchargement du PDF (ReportCardsController.ReportCardWriterRoles).
        editingReportCardRemark: null, // { termId, disciplinaryMention, councilDecision, observations }
        isSavingReportCardRemark: false,
        reportCardRemarkErrors: {},

        // Modale d'aperçu et d'impression des documents officiels — état + méthodes étalés depuis
        // le moteur partagé (wwwroot/js/pdf-preview.js) ; voir openPdfModalWithBlob ci-dessous.
        ...window.pdfPreview.state(),

        // Engagement financier (échéancier) : pas de liste persistée côté API (une seule note peut être
        // consultée par id) — on garde en mémoire l'id du dernier engagement créé pour proposer son
        // téléchargement immédiat, plutôt qu'un historique rechargé à l'ouverture de la fiche.
        isCommitmentModalOpen: false,
        isSavingCommitment: false,
        commitmentForm: { amount: '', dueDate: '', terms: '' },
        commitmentErrors: {},
        createdCommitmentId: null,

        // Slide-over state
        isCreateOpen: false,
        isSubmitting: false,
        newStudent: {
            fullName: '',
            birthDate: '',
            birthPlace: '',
            gender: 'M',
            classroomId: '', // Must be UUID
            photoUrl: '',
            photoData: '', // Feature B — base64 déjà compressé (photo-compress.js), rempli par <photo-dropzone>.
            guardianName: '',
            guardianPhone: '',
            guardianEmail: '',
            address: ''
        },
        createErrors: {},

        // Confirmation « Élève ajouté » affichée après un enregistrement réussi.
        showAddedDialog: false,
        addedStudentName: '',

        // Créer un élève, corriger/archiver une fiche, et gérer le cycle de vie d'une inscription
        // (annuler, déclarer un abandon/transfert) sont réservés au Directeur et au Secrétariat côté
        // serveur (StudentsController.ManageRoles, EnrollmentsController.EnrollmentWriters) —
        // l'Enseignant ne fait que consulter ses classes ; confort d'affichage, la protection réelle
        // est côté API.
        canManageStudent: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // Export PDF (docs/Volume_7_Security.md §15, matrice Élèves) : Directeur, Secrétariat et
        // Finance — jamais l'Enseignant, contrairement à la lecture (StudentsController.ExportRoles).
        canExportStudents: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat' || window.auth.role === 'Finance',

        // Saisir les observations du conseil (Blâme… Félicitations) est ouvert au Directeur, à
        // l'Enseignant ET au Secrétariat côté serveur (ReportCardsController.ReportCardWriterRoles) —
        // le Secrétariat assure ainsi le suivi administratif au même titre que la direction.
        // Jamais l'Enseignant (Volume 7 « Finance ») : studentDetail.payments vaut alors null côté API
        // (GetStudentDetailQueryHandler) — masquer l'onglet ici n'est qu'un confort d'affichage, la
        // protection réelle est l'absence de la donnée dans la réponse, pas ce booléen.
        canViewPayments: window.auth.role !== 'Enseignant',

        canEditReportCardRemark: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant' || window.auth.role === 'Secretariat',

        // Télécharger le bulletin PDF (JGK-G03) est ouvert au Directeur, à l'Enseignant ET au
        // Secrétariat côté serveur (ReportCardsController.ReportCardDownloadRoles) — il compose les
        // bulletins mais n'y écrit rien.
        canDownloadReportCard: window.auth.role === 'Directeur' || window.auth.role === 'Enseignant' || window.auth.role === 'Secretariat',

        // Édition de la fiche (modale). Sourcée depuis studentDetail.identity (fraîchement chargée,
        // RowVersion inclus) plutôt que la ligne de liste `detailStudent`, qui peut être périmée et ne
        // porte pas le jeton de concurrence — le bouton n'est donc proposé qu'une fois studentDetail chargé.
        //
        // JAMAIS null (bug JGK-UI02-BIS) : modal-shell affiche/masque au x-show (CSS display), pas au
        // x-if — le corps du formulaire reste donc DANS LE DOM, et Alpine continue d'évaluer chaque
        // x-model="editingStudent.xxx" en arrière-plan même modale fermée. Avec editingStudent = null,
        // chacun de ces bindings levait « Cannot read properties of null » à chaque tick réactif — pour
        // TOUT rôle affichant l'écran, bien avant l'ouverture de la modale, pas seulement en lecture
        // seule. isEditingStudentOpen porte désormais l'état ouvert/fermé (voir modal-shell open="…" sur
        // Views/Students/Index.cshtml) ; editingStudent garde une forme vide stable entre deux éditions.
        editingStudent: emptyEditingStudent(),
        isEditingStudentOpen: false,
        isSavingStudentEdit: false,
        studentEditErrors: {},
        showStudentEditedDialog: false,

        // Feature B — upload/retrait de la photo (fiche déjà créée) : commande dédiée, auto-enregistrée
        // dès le dépôt du fichier, séparée du bouton « Enregistrer » général (voir uploadStudentPhoto).
        photoUploadError: null,

        // Suppression de la fiche (modale de confirmation)
        deletingStudentRecord: null, // { id, fullName, rowVersion }
        isDeletingStudentRecord: false,
        deleteStudentRecordError: null,
        showStudentDeletedDialog: false,

        // Cycle de vie d'une inscription : annulation (erreur de saisie)
        cancelingEnrollment: null, // { enrollmentId, schoolYearLabel, rowVersion }
        isCancelingEnrollment: false,
        cancelEnrollmentError: null,

        // Cycle de vie d'une inscription : abandon / transfert en cours d'année
        // JAMAIS null — même raison que editingStudent ci-dessus (modal-shell est un x-show, pas un
        // x-if) : select-field model="changingEnrollmentStatus.newStatus" throw sinon en arrière-plan.
        changingEnrollmentStatus: emptyChangingEnrollmentStatus(),
        isChangingEnrollmentStatusOpen: false,
        isChangingEnrollmentStatus: false,
        changeEnrollmentStatusError: null,

        // Import de masse (rentrée scolaire, fichier CSV/Excel) : aperçu (dryRun=true) AVANT toute
        // écriture, puis confirmation (dryRun=false) sur LE MÊME fichier — voir ImportStudentsCommand.
        isImportOpen: false,
        importDragging: false,
        importFile: null, // File brut choisi/déposé, renvoyé tel quel au serveur (aucune transformation cliente).
        importFileName: '',
        isPreviewing: false,
        isImporting: false,
        importPreview: null, // Dernière réponse dryRun=true (ImportStudentsResult) : { totalRows, validRows, invalidRows, rows }
        importResult: null, // Réponse de la confirmation (dryRun=false) une fois committed=true.
        importError: null, // Rejet global (extension non supportée, fichier vide/corrompu, >1000 lignes).

        // Initialisation
        init() {
            const urlParams = new URLSearchParams(window.location.search);
            if (urlParams.has('classroomId')) {
                this.classroomFilter = urlParams.get('classroomId');
            }
            this.loadClassrooms();
            this.loadStudents();
        },

        async loadClassrooms() {
            try {
                const data = await window.api.get('/classrooms');
                this.classrooms = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error("Erreur chargement classes:", err);
            }
        },

        async loadStudents() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                if (this.search.trim()) params.set('search', this.search.trim());
                if (this.classroomFilter) params.set('classroomId', this.classroomFilter);
                if (this.genderFilter) params.set('gender', this.genderFilter);
                if (this.yearScope === 'active') params.set('activeYearOnly', 'true');

                const data = await window.api.get(`/students?${params.toString()}`);
                this.students = data.items || [];
                this.totalCount = data.totalCount || 0;
                this.girlsCount = data.girlsCount || 0;
                this.boysCount = data.boysCount || 0;
                this.newEnrollmentsCount = data.newEnrollmentsCount || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors du chargement des élèves.");
            } finally {
                this.isLoading = false;
            }
        },

        /** Un changement de filtre repart de la page 1 : la page 3 d'une recherche précédente n'a pas de sens ici. */
        applyFilters() {
            this.page = 1;
            this.loadStudents();
        },

        resetFilters() {
            this.search = '';
            this.classroomFilter = '';
            this.genderFilter = '';
            this.yearScope = 'active'; // on revient à la vue par défaut (inscrits de l'année active), pas à « tous »
            this.applyFilters();
        },

        /**
         * Export PDF (GET /students/export/pdf) : reprend les filtres Classe et Année actuellement
         * affichés à l'écran — le PDF reflète alors exactement la liste que l'utilisateur regarde,
         * pas un export « tout le monde » surprenant. La recherche texte et le genre, eux, ne sont
         * pas repris : ce sont des filtres d'écran, pas des critères d'un document imprimable.
         */
        async exportStudentsPdf() {
            const params = new URLSearchParams();
            if (this.classroomFilter) params.set('classroomId', this.classroomFilter);
            if (this.yearScope === 'active') params.set('activeYearOnly', 'true');

            const classroom = this.classrooms.find(c => c.id === this.classroomFilter);
            const title = classroom ? `Liste des élèves — ${classroom.name}` : 'Liste des élèves';
            const dateSuffix = new Date().toISOString().slice(0, 10);

            await this.openPdfModalWithBlob(
                `/api/v1/students/export/pdf?${params.toString()}`,
                title,
                `Eleves-${dateSuffix}.pdf`
            );
        },

        /**
         * Ouvre la fiche élève (JGK-D02). La ligne de liste (StudentListItem) affiche l'identité
         * immédiatement ; la fiche complète — historique scolaire, notes par trimestre, paiements —
         * est chargée depuis GET /students/{id}. L'onglet repart toujours sur « Historique ».
         */
        async openDetail(student) {
            // Nettoyage global : fermer toutes les modales actives
            if (window.closeAllModals) window.closeAllModals();

            this.detailStudent = student;
            this.studentDetail = null;
            this.detailError = null;
            this.detailTab = 'history';
            this.isLoadingDetails = true;
            try {
                this.studentDetail = await window.api.get(`/students/${student.id}`);
            } catch (err) {
                this.detailError = window.api.toMessage(err, "Impossible de charger la fiche complète de l'élève.");
            } finally {
                this.isLoadingDetails = false;
            }
        },

        closeDetail() {
            this.detailStudent = null;
            this.studentDetail = null;
            this.detailError = null;
        },

        /** Recharge la fiche (identité, historique, notes, paiements) sans fermer la modale de détail. */
        async refreshStudentDetail() {
            if (!this.detailStudent) return;
            this.studentDetail = await window.api.get(`/students/${this.detailStudent.id}`);
            // La ligne de liste sert encore à l'en-tête de la modale (voir la vue) : on la resynchronise
            // avec l'identité fraîchement rechargée pour qu'un champ modifié s'y reflète immédiatement.
            Object.assign(this.detailStudent, this.studentDetail.identity);
        },

        // ---------------------------------------------- Identifiant National de l'Élève (JGK-M01)

        openIenModal() {
            const identity = this.studentDetail?.identity;
            // Pré-remplit avec le numéro OFFICIEL en place ; jamais avec un provisoire (le champ sert
            // à saisir le vrai numéro, pas à recopier celui de secours).
            this.ienModal = {
                open: true,
                official: identity && identity.ienNumber && !identity.isIenProvisional ? identity.ienNumber : '',
                busy: false,
                provisioning: false,
                error: null
            };
        },

        closeIenModal() { this.ienModal.open = false; },

        /** Enregistre l'IEN OFFICIEL saisi (reçu de l'IEF). */
        async saveOfficialIen() {
            const value = this.ienModal.official.trim();
            if (!value || this.ienModal.busy || this.ienModal.provisioning) return;

            this.ienModal.busy = true;
            this.ienModal.error = null;
            try {
                await window.api.put(
                    `/state-integration/students/${this.detailStudent.id}/ien`,
                    { ienNumber: value }
                );
                this.ienModal.open = false;
                await this.refreshStudentDetail();
            } catch (err) {
                this.ienModal.error = window.api.toMessage(err, "Erreur lors de l'enregistrement de l'IEN.");
            } finally {
                this.ienModal.busy = false;
            }
        },

        /** Demande la génération d'un IEN PROVISOIRE de secours (corps sans numéro). */
        async generateProvisionalIen() {
            if (this.ienModal.busy || this.ienModal.provisioning) return;

            this.ienModal.provisioning = true;
            this.ienModal.error = null;
            try {
                await window.api.put(
                    `/state-integration/students/${this.detailStudent.id}/ien`,
                    { ienNumber: null }
                );
                this.ienModal.open = false;
                await this.refreshStudentDetail();
            } catch (err) {
                this.ienModal.error = window.api.toMessage(err, "Erreur lors de la génération d'un IEN provisoire.");
            } finally {
                this.ienModal.provisioning = false;
            }
        },

        // ------------------------------------------------------------ Bulletin PDF (JGK-G03)

        /**
         * Ouvre l'APERÇU du bulletin PDF d'un trimestre dans la modale partagée (impression et
         * téléchargement depuis son en-tête) — plus de téléchargement direct « à l'aveugle ».
         * Le bulletin est produit par POST /report-cards/generate (corps { studentId, termId }) :
         * le moteur d'aperçu partagé sait désormais servir une route POST à corps JSON.
         */
        async previewReportCard(term) {
            if (!this.detailStudent) return;
            this.reportCardError = null;
            this.downloadingTermId = term.termId;
            try {
                const matricule = this.studentDetail?.identity?.matricule || this.detailStudent.matricule;
                await this.openPdfPreview(
                    '/api/v1/report-cards/generate',
                    `Bulletin — ${term.termLabel}`,
                    `Bulletin-${matricule}-${term.termLabel.replace(/\s+/g, '-')}.pdf`,
                    {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ studentId: this.detailStudent.id, termId: term.termId })
                    }
                );
            } catch (err) {
                this.reportCardError = window.api.toMessage(err, 'Aperçu du bulletin impossible.');
            } finally {
                this.downloadingTermId = null;
            }
        },

        async sendReportCard(term, channel) {
            if (!this.detailStudent) return;
            this.reportCardError = null;
            this.sendingTermId = term.termId;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch('/api/v1/report-cards/send', {
                    method: 'POST',
                    headers: {
                        Authorization: `Bearer ${window.auth.accessToken}`,
                        'Content-Type': 'application/json'
                    },
                    credentials: 'same-origin',
                    body: JSON.stringify({ studentId: this.detailStudent.id, termId: term.termId, channel: channel })
                });

                if (!response.ok) {
                    const err = await response.json().catch(() => ({ message: 'Envoi du bulletin impossible.' }));
                    throw new Error(err.message);
                }

                // Show success notification or rely on a global toast if one exists, else alert
                alert('Bulletin envoyé avec succès.');

            } catch (err) {
                this.reportCardError = window.api.toMessage(err, 'Envoi du bulletin impossible.');
                alert(this.reportCardError);
            } finally {
                this.sendingTermId = null;
            }
        },

        /** Ouvre l'écran, préreempli depuis GET /report-cards/remark (vide si rien n'a encore été saisi). */
        async openReportCardRemark(term) {
            if (!this.detailStudent) return;
            this.reportCardRemarkErrors = {};
            this.editingReportCardRemark = { termId: term.termId, disciplinaryMention: '', councilDecision: '', observations: '' };
            try {
                const remark = await window.api.get(`/report-cards/remark?studentId=${this.detailStudent.id}&termId=${term.termId}`);
                this.editingReportCardRemark = {
                    termId: term.termId,
                    disciplinaryMention: remark.disciplinaryMention || '',
                    councilDecision: remark.councilDecision || '',
                    observations: remark.observations || ''
                };
            } catch (err) {
                this.reportCardRemarkErrors = { global: window.api.toMessage(err, 'Impossible de charger les observations du conseil.') };
            }
        },

        closeReportCardRemark() {
            this.editingReportCardRemark = null;
            this.reportCardRemarkErrors = {};
        },

        async submitReportCardRemark() {
            if (!this.editingReportCardRemark || !this.detailStudent) return;

            this.isSavingReportCardRemark = true;
            this.reportCardRemarkErrors = {};
            try {
                await window.api.put('/report-cards/remark', {
                    studentId: this.detailStudent.id,
                    termId: this.editingReportCardRemark.termId,
                    disciplinaryMention: this.editingReportCardRemark.disciplinaryMention || null,
                    councilDecision: this.editingReportCardRemark.councilDecision || null,
                    observations: this.editingReportCardRemark.observations || null
                });
                this.closeReportCardRemark();
            } catch (err) {
                this.reportCardRemarkErrors = window.api.toFieldErrors(err, 'Erreur lors de l\'enregistrement des observations.');
            } finally {
                this.isSavingReportCardRemark = false;
            }
        },

        // ------------------------------------------------------------ Modifier la fiche

        openEditStudent() {
            if (!this.studentDetail) return;
            const identity = this.studentDetail.identity;
            this.editingStudent = {
                fullName: identity.fullName,
                birthDate: identity.birthDate,
                birthPlace: identity.birthPlace || '',
                gender: identity.gender,
                classroomId: identity.classroomId,
                photoUrl: identity.photoUrl || '', // URL brute, jamais la photo téléversée (round-trip fidèle).
                photoDisplayUrl: identity.photoDisplayUrl || '', // Aperçu <photo-dropzone> uniquement.
                guardianName: identity.guardianName || '',
                guardianPhone: identity.guardianPhone || '',
                guardianEmail: identity.guardianEmail || '',
                address: identity.address || '',
                rowVersion: identity.rowVersion
            };
            this.isEditingStudentOpen = true;
            this.studentEditErrors = {};
            this.photoUploadError = null;
        },

        closeEditStudent() {
            this.isEditingStudentOpen = false;
            this.editingStudent = emptyEditingStudent();
            this.studentEditErrors = {};
            this.photoUploadError = null;
        },

        /**
         * Feature B — dépôt/retrait de la photo depuis la fiche déjà créée : appelle IMMÉDIATEMENT
         * PUT /students/{id}/photo (commande dédiée, SetStudentPhotoCommand), sans attendre le bouton
         * « Enregistrer » général — mélanger la photo dans la sauvegarde générale la ferait perdre
         * silencieusement à la moindre modification de nom/classe qui omettrait de la retransmettre.
         */
        async uploadStudentPhoto(photoBase64) {
            if (!this.isEditingStudentOpen || !this.detailStudent) return;

            this.photoUploadError = null;
            try {
                const result = await window.api.put(`/students/${this.detailStudent.id}/photo`, {
                    photoData: photoBase64,
                    rowVersion: this.editingStudent.rowVersion
                });
                this.editingStudent.photoDisplayUrl = result.photoDisplayUrl || '';
                this.editingStudent.rowVersion = result.rowVersion;
                await this.refreshStudentDetail(); // synchronise la vignette de l'avatar en tête de fiche
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.photoUploadError = 'Cette fiche vient d\'être modifiée par un autre utilisateur. Elle a été rafraîchie — réessayez.';
                    await this.refreshStudentDetail();
                    this.editingStudent.rowVersion = this.studentDetail.identity.rowVersion;
                } else {
                    this.photoUploadError = (err && err.message) || 'Erreur lors de l\'envoi de la photo.';
                }
            }
        },

        async submitEditStudent() {
            if (!this.isEditingStudentOpen || !this.detailStudent) return;

            this.isSavingStudentEdit = true;
            this.studentEditErrors = {};
            try {
                await window.api.put(`/students/${this.detailStudent.id}`, this.editingStudent);
                this.closeEditStudent();
                await this.refreshStudentDetail();
                await this.loadStudents();
                this.showStudentEditedDialog = true;
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    // Verrou optimiste (AGENTS.md règle #5) : on recharge la fiche pour montrer l'état
                    // réel avant de laisser l'utilisateur réessayer, jamais un écrasement silencieux.
                    this.studentEditErrors = { global: 'Cette fiche vient d\'être modifiée par un autre utilisateur. Elle a été rafraîchie — vérifiez les valeurs puis réessayez.' };
                    await this.refreshStudentDetail();
                } else {
                    this.studentEditErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingStudentEdit = false;
            }
        },

        // ------------------------------------------------------------ Supprimer la fiche

        openDeleteStudent() {
            if (!this.studentDetail || !this.detailStudent) return;
            this.deletingStudentRecord = {
                id: this.detailStudent.id,
                fullName: this.studentDetail.identity.fullName,
                rowVersion: this.studentDetail.identity.rowVersion
            };
            this.deleteStudentRecordError = null;
        },

        closeDeleteStudent() {
            this.deletingStudentRecord = null;
            this.deleteStudentRecordError = null;
        },

        async confirmDeleteStudent() {
            if (!this.deletingStudentRecord) return;

            this.isDeletingStudentRecord = true;
            this.deleteStudentRecordError = null;
            try {
                await window.api.delete(`/students/${this.deletingStudentRecord.id}?rowVersion=${this.deletingStudentRecord.rowVersion}`);
                this.addedStudentName = ''; // évite d'afficher un nom périmé dans une autre confirmation
                this.deletingStudentRecord = null;
                this.closeDetail();
                this.page = 1;
                await this.loadStudents();
                this.showStudentDeletedDialog = true;
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    // Une inscription ou une note existe déjà (DeleteStudentCommandHandler) : le
                    // message serveur est déjà explicite, on l'affiche tel quel.
                    this.deleteStudentRecordError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteStudentRecordError = 'Cette fiche vient d\'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.';
                    await this.refreshStudentDetail();
                } else {
                    this.deleteStudentRecordError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingStudentRecord = false;
            }
        },

        // ------------------------------------------------------------ Inscriptions : annulation (erreur de saisie)

        openCancelEnrollment(entry) {
            this.cancelingEnrollment = {
                enrollmentId: entry.enrollmentId,
                schoolYearLabel: entry.schoolYearLabel,
                rowVersion: entry.rowVersion
            };
            this.cancelEnrollmentError = null;
        },

        closeCancelEnrollment() {
            this.cancelingEnrollment = null;
            this.cancelEnrollmentError = null;
        },

        async confirmCancelEnrollment() {
            if (!this.cancelingEnrollment) return;

            this.isCancelingEnrollment = true;
            this.cancelEnrollmentError = null;
            try {
                await window.api.delete(`/enrollments/${this.cancelingEnrollment.enrollmentId}?rowVersion=${this.cancelingEnrollment.rowVersion}`);
                this.cancelingEnrollment = null;
                await this.refreshStudentDetail();
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    // Un reçu de paiement existe déjà pour cette inscription (CancelEnrollmentCommandHandler) :
                    // le message serveur invite déjà à utiliser le changement de statut à la place.
                    this.cancelEnrollmentError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.cancelEnrollmentError = 'Cette inscription vient d\'être modifiée par un autre utilisateur. La fiche a été rafraîchie.';
                    await this.refreshStudentDetail();
                } else {
                    this.cancelEnrollmentError = (err && err.message) || "Erreur lors de l'annulation.";
                }
            } finally {
                this.isCancelingEnrollment = false;
            }
        },

        // ------------------------------------------------------------ Inscriptions : abandon / transfert

        openChangeEnrollmentStatus(entry) {
            this.changingEnrollmentStatus = {
                enrollmentId: entry.enrollmentId,
                schoolYearLabel: entry.schoolYearLabel,
                rowVersion: entry.rowVersion,
                newStatus: 'DroppedOut'
            };
            this.isChangingEnrollmentStatusOpen = true;
            this.changeEnrollmentStatusError = null;
        },

        closeChangeEnrollmentStatus() {
            this.isChangingEnrollmentStatusOpen = false;
            this.changingEnrollmentStatus = emptyChangingEnrollmentStatus();
            this.changeEnrollmentStatusError = null;
        },

        async submitChangeEnrollmentStatus() {
            if (!this.isChangingEnrollmentStatusOpen) return;

            this.isChangingEnrollmentStatus = true;
            this.changeEnrollmentStatusError = null;
            try {
                await window.api.post(`/enrollments/${this.changingEnrollmentStatus.enrollmentId}/status`, {
                    newStatus: this.changingEnrollmentStatus.newStatus,
                    rowVersion: this.changingEnrollmentStatus.rowVersion
                });
                this.closeChangeEnrollmentStatus();
                await this.refreshStudentDetail();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.changeEnrollmentStatusError = 'Cette inscription vient d\'être modifiée par un autre utilisateur. La fiche a été rafraîchie.';
                    await this.refreshStudentDetail();
                } else {
                    this.changeEnrollmentStatusError = window.api.toFieldErrors(err, 'Erreur lors du changement de statut.').global
                        || (err && err.message) || 'Erreur lors du changement de statut.';
                }
            } finally {
                this.isChangingEnrollmentStatus = false;
            }
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                const created = await window.api.post('/students', this.newStudent);

                // Fermer la modale et réinitialiser
                this.isCreateOpen = false;
                this.addedStudentName = this.newStudent.fullName;
                this.newStudent = { fullName: '', birthDate: '', birthPlace: '', gender: 'M', classroomId: '', photoUrl: '', photoData: '', guardianName: '', guardianPhone: '', guardianEmail: '', address: '' };

                // Rafraîchir la liste
                this.page = 1;
                await this.loadStudents();
                this.showAddedDialog = true; // confirmation « Élève ajouté »
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(
                    err, "Une erreur est survenue lors de la création.");
            } finally {
                this.isSubmitting = false;
            }
        },

        // ------------------------------------------------------------ Import de masse (rentrée scolaire)

        openImport() {
            this.isImportOpen = true;
            this.resetImportState();
        },

        closeImport() {
            this.isImportOpen = false;
            this.resetImportState();
            // La liste peut avoir grossi (import confirmé pendant que la modale était ouverte) : on la
            // recharge systématiquement à la fermeture plutôt que de suivre chaque cas un par un.
            this.page = 1;
            this.loadStudents();
        },

        resetImportState() {
            this.importDragging = false;
            this.importFile = null;
            this.importFileName = '';
            this.isPreviewing = false;
            this.isImporting = false;
            this.importPreview = null;
            this.importResult = null;
            this.importError = null;
        },

        /** Un fichier choisi (clic) ou déposé (drag&drop) lance IMMÉDIATEMENT l'aperçu — pas de bouton intermédiaire. */
        onImportFileSelected(file) {
            if (!file) return;
            this.importFile = file;
            this.importFileName = file.name;
            this.importPreview = null;
            this.importResult = null;
            this.importError = null;
            this.previewImport();
        },

        /** dryRun=true : valide l'intégralité du fichier SANS RIEN écrire (voir ImportStudentsCommand). */
        async previewImport() {
            if (!this.importFile) return;

            this.isPreviewing = true;
            this.importError = null;
            try {
                const formData = new FormData();
                formData.append('file', this.importFile);
                formData.append('dryRun', 'true');
                this.importPreview = await window.api.upload('/students/import', formData);
            } catch (err) {
                this.importError = (err && err.message) || "Erreur lors de l'analyse du fichier.";
            } finally {
                this.isPreviewing = false;
            }
        },

        /**
         * dryRun=false, sur LE MÊME fichier déjà prévisualisé : le serveur re-valide intégralement (l'état
         * a pu changer depuis l'aperçu — classe supprimée entre-temps, par ex.) et n'écrit QUE si le
         * fichier est encore entièrement valide, en une seule transaction (aucun import partiel, même en
         * cas de coupure réseau après l'envoi : soit la réponse n'arrive jamais et rien n'a été écrit,
         * soit elle arrive et tout est déjà en base).
         */
        async confirmImport() {
            if (!this.importFile || !this.importPreview || this.importPreview.invalidRows > 0) return;

            this.isImporting = true;
            this.importError = null;
            try {
                const formData = new FormData();
                formData.append('file', this.importFile);
                formData.append('dryRun', 'false');
                this.importResult = await window.api.upload('/students/import', formData);
            } catch (err) {
                // Un rejet ici (422) signifie que l'état a changé depuis l'aperçu (ex. classe supprimée
                // entre-temps) : on relance un aperçu pour montrer la situation à jour plutôt que de
                // laisser l'utilisateur face à une erreur générique sans détail ligne par ligne.
                this.importError = (err && err.message) || "Erreur lors de l'import.";
                await this.previewImport();
            } finally {
                this.isImporting = false;
            }
        },

        /** Bouton « Télécharger le modèle » : même mécanique fetch + blob que les autres téléchargements Excel. */
        async downloadImportTemplate() {
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch('/api/v1/students/import/template', {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) throw new Error('Téléchargement du modèle impossible.');

                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = 'Modele-Import-Eleves.xlsx';
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                this.importError = (err && err.message) || 'Téléchargement du modèle impossible.';
            }
        },

        /** Classes Tailwind d'une cellule de l'aperçu : rouge si CE champ précis est en erreur. */
        importCellClass(row, field) {
            return row.fieldErrors && row.fieldErrors[field]
                ? 'bg-danger-bg text-danger font-medium'
                : 'text-gray-700';
        },

        // Utilitaires de présentation
        formatDate(dateStr) {
            if (!dateStr) return '';
            const date = new Date(dateStr);
            return date.toLocaleDateString('fr-FR');
        },
        
        formatPhoneSenegal(number) {
            if (!number) return '—';
            const cleaned = number.toString().replace(/\D/g, '');
            let local = cleaned;
            if (local.startsWith('221') && local.length === 12) {
                local = local.substring(3);
            }
            if (local.length === 9) {
                return local.replace(/(\d{2})(\d{3})(\d{2})(\d{2})/, '$1 $2 $3 $4');
            }
            return number;
        },
        
        getInitials(name) {
            if (!name) return '??';
            return name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
        },

        /**
         * Couleur douce du badge d'initiales, DÉTERMINISTE par nom (charte D-MAJ §2 : « humaniser
         * l'affichage »). Un même élève garde toujours la même teinte, d'un écran à l'autre et d'un
         * rechargement à l'autre. Les chaînes de classes sont écrites en TOUTES LETTRES (jamais
         * concaténées) pour que le scanner Tailwind les compile — sinon elles seraient purgées.
         */
        initialsClasses(name) {
            const palette = [
                'bg-blue-100 text-blue-700',
                'bg-purple-100 text-purple-700',
                'bg-pink-100 text-pink-700',
                'bg-emerald-100 text-emerald-700',
                'bg-amber-100 text-amber-700',
                'bg-indigo-100 text-indigo-700',
                'bg-teal-100 text-teal-700',
                'bg-rose-100 text-rose-700'
            ];
            if (!name) return palette[0];
            let hash = 0;
            for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) | 0;
            return palette[Math.abs(hash) % palette.length];
        },

        // ----- Fiche élève (JGK-D02) : utilitaires de présentation -----

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatAmount(amount) {
            if (amount === null || amount === undefined) return '—';
            return window.formatFCFA(amount);
        },

        /**
         * Note lisible : au plus une décimale, sans zéro inutile (17.0 → « 17 », 14.5 → « 14,5 »),
         * même convention que le bulletin (DDS §8). Renvoie « — » si la note est absente.
         */
        formatGrade(value) {
            if (value === null || value === undefined) return '—';
            return (Math.round(value * 10) / 10).toLocaleString('fr-FR');
        },

        /** Moyenne suffixée du barème du CYCLE (14,5/20 au secondaire, 7,2/10 en primaire) — jamais « /20 » supposé. */
        formatAverage(value) {
            if (value === null || value === undefined) return '—';
            const scale = this.studentDetail?.gradingScale ?? 20;
            return `${this.formatGrade(value)}/${scale}`;
        },

        /**
         * Cycle Primaire (barème /10) : gradingScale, résolu par cycle côté serveur
         * (GetStudentDetailQueryHandler), vaut 10 pour le seul Primaire, 20 pour le secondaire. L'élève
         * n'étant que dans UNE classe, toute la fiche porte un seul cycle — signal fiable pour verrouiller
         * l'affichage des coefficients et adapter les libellés (formatAverage s'en sert déjà).
         */
        get isPrimaire() {
            return this.studentDetail?.gradingScale === 10;
        },

        /**
         * Inscription ACTIVE de l'année en cours (au plus une par élève) : la Sommation impayés et
         * l'Engagement financier sont rattachés à UNE inscription (Finance/DuesNotice, Finance/Commitment),
         * contrairement au récapitulatif financier ci-dessus qui agrège toutes les années.
         */
        get activeEnrollmentId() {
            return this.studentDetail?.academicHistory?.find(e => e.isActiveYear)?.enrollmentId || null;
        },

        /**
         * Coefficient AFFICHÉ, verrouillé à 1 en Primaire : ce cycle n'a pas de système de coefficients
         * (le serveur les renvoie déjà à 1, ce garde-fou empêche tout coefficient pondéré résiduel de
         * s'afficher) — cohérent avec la moyenne simple /10 et le bulletin primaire.
         */
        coefficientDisplay(subject) {
            return this.isPrimaire ? 1 : subject.coefficient;
        },

        enrollmentTypeLabel(type) {
            return { NewEnrollment: 'Nouvelle inscription', ReEnrollment: 'Réinscription' }[type] || type;
        },

        enrollmentStatusLabel(status) {
            return {
                Confirmed: 'Confirmée', Pending: 'En attente', Cancelled: 'Annulée',
                DroppedOut: 'Abandon', Transferred: 'Transféré(e)'
            }[status] || status;
        },

        /** Classe de pastille partagée (input.css) selon le statut d'inscription. */
        enrollmentStatusBadge(status) {
            return {
                Confirmed: 'status-badge-success',
                Pending: 'status-badge-warning',
                Cancelled: 'status-badge-danger',
                DroppedOut: 'status-badge-danger',
                Transferred: 'status-badge-neutral'
            }[status] || 'status-badge-neutral';
        },

        /** Une inscription encore « vivante » (ni annulée, ni déjà en abandon/transfert) peut transiter. */
        canTransitionEnrollment(status) {
            return status === 'Confirmed' || status === 'Pending';
        },

        enrollmentNewStatusLabel(status) {
            return { DroppedOut: 'Abandon', Transferred: 'Transfert' }[status] || status;
        },

        paymentMethodLabel(method) {
            return {
                Cash: 'Espèces', Cheque: 'Chèque', Transfer: 'Virement', MobileMoney: 'Mobile Money'
            }[method] || method;
        },

        paymentStatusLabel(status) {
            return { Paid: 'Soldé', Partial: 'Partiel', Cancelled: 'Annulé' }[status] || status;
        },

        paymentStatusBadge(status) {
            return {
                Paid: 'status-badge-success',
                Partial: 'status-badge-warning',
                Cancelled: 'status-badge-danger'
            }[status] || 'status-badge-neutral';
        },

        async openEnrollmentCertificatePreview(enrollmentId, yearLabel) {
            await this.openPdfModalWithBlob(
                `/api/v1/enrollments/${enrollmentId}/certificate/pdf`,
                `Attestation d'inscription (${yearLabel})`,
                `Attestation-Inscription-${enrollmentId}.pdf`
            );
        },

        async openEnrollmentReceiptPreview(enrollmentId, yearLabel) {
            await this.openPdfModalWithBlob(
                `/api/v1/enrollments/${enrollmentId}/receipt/pdf`,
                `Reçu d'inscription (${yearLabel})`,
                `Recu-Inscription-${enrollmentId}.pdf`
            );
        },

        async openPaymentReceiptPreview(paymentId, receiptNumber) {
            await this.openPdfModalWithBlob(
                `/api/v1/finance/payments/${paymentId}/receipt/pdf`,
                `Reçu de paiement n° ${receiptNumber}`,
                `Recu-${receiptNumber}.pdf`
            );
        },

        async openExeatCertificatePreview(enrollmentId, yearLabel) {
            await this.openPdfModalWithBlob(
                `/api/v1/enrollments/${enrollmentId}/exeat/pdf`,
                `Certificat d'exéat (${yearLabel})`,
                `Exeat-${enrollmentId}.pdf`
            );
        },

        async openDuesNoticePreview(enrollmentId) {
            await this.openPdfModalWithBlob(
                `/api/v1/finance/enrollments/${enrollmentId}/dues-notice/pdf`,
                'Sommation pour impayés',
                `Sommation-${enrollmentId}.pdf`
            );
        },

        openCreateCommitmentModal(enrollmentId) {
            this.commitmentForm = { enrollmentId, amount: '', dueDate: '', terms: '' };
            this.commitmentErrors = {};
            this.isCommitmentModalOpen = true;
        },

        async submitCreateCommitment() {
            this.isSavingCommitment = true;
            this.commitmentErrors = {};
            try {
                const id = await window.api.post('/finance/commitments', {
                    enrollmentId: this.commitmentForm.enrollmentId,
                    amount: Number(this.commitmentForm.amount) || 0,
                    dueDate: this.commitmentForm.dueDate,
                    terms: this.commitmentForm.terms
                });
                this.createdCommitmentId = id;
                this.isCommitmentModalOpen = false;
                toast.success('Engagement financier enregistré.');
            } catch (err) {
                this.commitmentErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement de l'engagement.");
            } finally {
                this.isSavingCommitment = false;
            }
        },

        async openCommitmentPreview(commitmentId) {
            await this.openPdfModalWithBlob(
                `/api/v1/finance/commitments/${commitmentId}/pdf`,
                'Engagement financier',
                `Engagement-${commitmentId}.pdf`
            );
        },

        // closePdfPreview / printPreviewPdf / downloadPreviewPdf : voir window.pdfPreview.state().
    }));
});
