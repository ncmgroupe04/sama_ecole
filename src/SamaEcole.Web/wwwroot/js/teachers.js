/**
 * JGK-D03/D04 — Enseignants. Liste paginée + recherche (GET /teachers), création de fiche
 * (POST /teachers, matricule généré côté serveur), fiche détaillée (GET /teachers/{id}) avec matières
 * qualifiées et historique des affectations, et attribution classe/matière (POST /teachers/{id}/assignments).
 *
 * Permissions (confort d'affichage ; l'API garde reste la protection réelle) : Voir → Super Admin /
 * Directeur / Secrétariat ; Créer & Attribuer → Directeur / Secrétariat.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('teachersView', () => ({
        teachers: [],
        subjects: [],
        eligibleAccounts: [],
        totalCount: 0,
        page: 1,
        pageSize: 10,
        isLoading: false,
        error: null,
        search: '',

        // Aperçu/impression PDF partagé (closePdfPreview, printPreviewPdf, downloadPreviewPdf) —
        // même mixin que la liste des élèves, voir wwwroot/js/pdf-preview.js.
        ...window.pdfPreview.state(),

        canManage: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',
        canManageSchedule: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // Export PDF : mêmes rôles que la consultation de la liste (docs/Volume_7_Security.md
        // « Enseignants ») — un export n'ouvre aucune donnée que le rôle ne puisse déjà lire.
        canExportTeachers:
            window.auth.role === 'SuperAdmin' ||
            window.auth.role === 'Directeur' ||
            window.auth.role === 'Secretariat',
        
        // Emploi du Temps (Schedule)
        activeTab: 'teachers', // 'teachers' ou 'schedule'
        scheduleViewMode: 'teacher', // 'teacher' ou 'classroom'
        selectedScheduleTeacherId: '',
        selectedScheduleClassroomId: '',
        scheduleSlots: [],
        isLoadingSchedule: false,
        creatingScheduleSlot: false,
        isSubmittingSchedule: false,
        createScheduleError: null,
        scheduleForm: { teacherId: '', classroomId: '', subjectId: '', dayOfWeek: 1, startTime: '08:00', endTime: '09:00', roomNumber: '' },
        deletingScheduleSlot: null,
        isDeletingSchedule: false,
        
        daysOfWeek: [
            { value: 1, label: 'Lundi' },
            { value: 2, label: 'Mardi' },
            { value: 3, label: 'Mercredi' },
            { value: 4, label: 'Jeudi' },
            { value: 5, label: 'Vendredi' },
            { value: 6, label: 'Samedi' }
        ],

        timeSlots: [
            { label: '08h00 - 09h00', start: '08:00', end: '09:00', isPause: false },
            { label: '09h00 - 10h00', start: '09:00', end: '10:00', isPause: false },
            { label: '10h00 - 11h00', start: '10:00', end: '11:00', isPause: false },
            { label: '11h00 - 12h00', start: '11:00', end: '12:00', isPause: false },
            { label: 'PAUSE MIDI', start: '12:00', end: '15:00', isPause: true },
            { label: '15h00 - 16h00', start: '15:00', end: '16:00', isPause: false },
            { label: '16h00 - 17h00', start: '16:00', end: '17:00', isPause: false },
            { label: '17h00 - 18h00', start: '17:00', end: '18:00', isPause: false },
        ],

        getSlotForDayAndTime(dayValue, startTime) {
            // Note : L'API retourne startTime au format "08:00:00". On le formate pour le comparer.
            return this.scheduleSlots.find(s => s.dayOfWeek === dayValue && s.startTime.substring(0, 5) === startTime);
        },

        get sortedScheduleSlots() {
            return this.scheduleSlots.sort((a, b) => {
                if (a.startTime !== b.startTime) return a.startTime.localeCompare(b.startTime);
                return a.dayOfWeek - b.dayOfWeek;
            });
        },
        // Seul le Directeur peut lister les comptes (GET /users) : le champ de rattachement ne s'affiche
        // donc que pour lui. Le Secrétariat crée la fiche sans lien ; le Directeur le posera plus tard.
        canLinkAccount: window.auth.role === 'Directeur',

        // Slide-over création
        isCreateOpen: false,
        isSubmitting: false,
        newTeacher: { fullName: '', email: '', phone: '', birthDate: '', birthPlace: '', address: '', photoUrl: '', photoData: '', subjectIds: [], userId: '' },
        createErrors: {},

        // Import de masse (corps professoral) : aperçu AVANT écriture — même contrat que students.js.
        isImportOpen: false,
        importDragging: false,
        importFile: null, // File brut choisi/déposé, renvoyé tel quel au serveur (aucune transformation cliente).
        importFileName: '',
        isPreviewing: false,
        isImporting: false,
        importPreview: null, // Dernière réponse dryRun=true (ImportTeachersResult) : { totalRows, validRows, invalidRows, rows }
        importResult: null, // Réponse de la confirmation (dryRun=false) une fois committed=true.
        importError: null, // Rejet global (extension non supportée, fichier vide/corrompu, >1000 lignes).

        // Confirmation « Enseignant ajouté » affichée après un enregistrement réussi.
        showAddedDialog: false,
        addedTeacherName: '',

        // Slide-over détail
        detail: null,
        detailLoading: false,
        assignForm: { classroomId: '', subjectId: '' },
        assignError: null,
        assignSubmitting: false,
        assignSuccess: false,
        classrooms: [],

        // Édition de la fiche (modale). `detail` est déjà la fiche fraîchement chargée (GET
        // /teachers/{id}), RowVersion inclus — pas besoin d'une source séparée comme pour l'élève.
        editingTeacher: null, // { fullName, email, phone, birthPlace, photoUrl, photoDisplayUrl, subjectIds, rowVersion }
        isSavingTeacherEdit: false,
        teacherEditErrors: {},
        showTeacherEditedDialog: false,

        // Feature B — upload/retrait de la photo (fiche déjà créée), même mécanique que students.js.
        photoUploadError: null,

        // Suppression de la fiche (modale de confirmation)
        deletingTeacherRecord: null, // { id, fullName, rowVersion }
        isDeletingTeacherRecord: false,
        deleteTeacherRecordError: null,
        showTeacherDeletedDialog: false,

        init() {
            this.loadSubjects();
            this.loadTeachers();
            if (this.canLinkAccount) this.loadEligibleAccounts();
            if (this.canManage) this.loadClassrooms();
        },

        async loadSubjects() {
            try {
                const data = await window.api.get('/subjects');
                this.subjects = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement matières:', err);
            }
        },

        async loadClassrooms() {
            try {
                const data = await window.api.get('/classrooms');
                this.classrooms = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement classes:', err);
            }
        },

        /** Comptes rattachables : rôle Enseignant, non déjà liés (le serveur refuse un doublon de toute façon). */
        async loadEligibleAccounts() {
            try {
                const users = await window.api.get('/users');
                this.eligibleAccounts = (users || []).filter((u) => u.role === 'Enseignant');
            } catch (err) {
                console.error('Erreur chargement comptes:', err);
            }
        },

        async loadTeachers() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                if (this.search.trim()) params.set('search', this.search.trim());

                const data = await window.api.get(`/teachers?${params.toString()}`);
                this.teachers = data.items || [];
                this.totalCount = data.totalCount || 0;
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des enseignants.';
            } finally {
                this.isLoading = false;
            }
        },

        applyFilters() {
            this.page = 1;
            this.loadTeachers();
        },

        resetFilters() {
            this.search = '';
            this.applyFilters();
        },

        /**
         * Export PDF « LISTE DES ENSEIGNANTS » (GET /teachers/export/pdf). La recherche texte n'est
         * PAS reprise, même raisonnement que pour les élèves : c'est un filtre d'écran, pas un
         * critère de document imprimable. Le PDF couvre donc tout le corps professoral, sans
         * pagination — ce que la liste écran ne peut pas montrer d'un seul tenant.
         */
        async exportTeachersPdf() {
            const dateSuffix = new Date().toISOString().slice(0, 10);

            await this.openPdfModalWithBlob(
                '/api/v1/teachers/export/pdf',
                'Liste des enseignants',
                `Enseignants-${dateSuffix}.pdf`
            );
        },

        // ------------------------------------------------------------ Import de masse (corps professoral)

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
            this.loadTeachers();
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

        /** dryRun=true : valide l'intégralité du fichier SANS RIEN écrire (voir ImportTeachersCommand). */
        async previewImport() {
            if (!this.importFile) return;

            this.isPreviewing = true;
            this.importError = null;
            try {
                const formData = new FormData();
                formData.append('file', this.importFile);
                formData.append('dryRun', 'true');
                this.importPreview = await window.api.upload('/teachers/import', formData);
            } catch (err) {
                this.importError = (err && err.message) || "Erreur lors de l'analyse du fichier.";
            } finally {
                this.isPreviewing = false;
            }
        },

        /**
         * dryRun=false, sur LE MÊME fichier déjà prévisualisé : le serveur re-valide intégralement (l'état
         * a pu changer depuis l'aperçu — un e-mail devenu en doublon, par ex.) et n'écrit QUE si le
         * fichier est encore entièrement valide, en une seule transaction (aucun import partiel).
         */
        async confirmImport() {
            if (!this.importFile || !this.importPreview || this.importPreview.invalidRows > 0) return;

            this.isImporting = true;
            this.importError = null;
            try {
                const formData = new FormData();
                formData.append('file', this.importFile);
                formData.append('dryRun', 'false');
                this.importResult = await window.api.upload('/teachers/import', formData);
            } catch (err) {
                // Un rejet ici (422) signifie que l'état a changé depuis l'aperçu : on relance un aperçu
                // pour montrer la situation à jour plutôt que de laisser une erreur générique sans détail.
                this.importError = (err && err.message) || "Erreur lors de l'import.";
                await this.previewImport();
            } finally {
                this.isImporting = false;
            }
        },

        /** Bouton « Télécharger le modèle » : même mécanique fetch+blob que students.js.downloadImportTemplate. */
        async downloadImportTemplate() {
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch('/api/v1/teachers/import/template', {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) throw new Error('Téléchargement du modèle impossible.');

                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = 'Modele-Import-Enseignants.xlsx';
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

        openCreate() {
            this.newTeacher = { fullName: '', email: '', phone: '', birthPlace: '', photoUrl: '', photoData: '', subjectIds: [], userId: '' };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                const payload = {
                    fullName: this.newTeacher.fullName,
                    email: this.newTeacher.email,
                    phone: this.newTeacher.phone || null,
                    birthDate: this.newTeacher.birthDate,
                    birthPlace: this.newTeacher.birthPlace || null,
                    address: this.newTeacher.address || null,
                    photoUrl: this.newTeacher.photoUrl || null,
                    photoData: this.newTeacher.photoData || null,
                    subjectIds: this.newTeacher.subjectIds,
                    userId: this.newTeacher.userId || null
                };
                await window.api.post('/teachers', payload);

                this.isCreateOpen = false;
                this.addedTeacherName = this.newTeacher.fullName;
                this.page = 1;
                await this.loadTeachers();
                this.showAddedDialog = true; // confirmation « Enseignant ajouté »
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, 'Une erreur est survenue lors de la création.');
            } finally {
                this.isSubmitting = false;
            }
        },

        async openDetail(teacher) {
            this.detail = null;
            this.assignForm = { classroomId: '', subjectId: '' };
            this.assignError = null;
            this.assignSuccess = false;
            this.detailLoading = true;
            try {
                this.detail = await window.api.get(`/teachers/${teacher.id}`);
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement de la fiche.';
            } finally {
                this.detailLoading = false;
            }
        },

        closeDetail() {
            this.detail = null;
        },

        /** Recharge la fiche depuis GET /teachers/{id} sans fermer la modale de détail. */
        async refreshTeacherDetail() {
            if (!this.detail) return;
            this.detail = await window.api.get(`/teachers/${this.detail.id}`);
        },

        // ------------------------------------------------------------ Modifier la fiche

        openEditTeacher() {
            if (!this.detail) return;
            this.editingTeacher = {
                fullName: this.detail.fullName,
                email: this.detail.email,
                phone: this.detail.phone || '',
                birthDate: this.detail.birthDate,
                birthPlace: this.detail.birthPlace || '',
                address: this.detail.address || '',
                photoUrl: this.detail.photoUrl || '', // URL brute, jamais la photo téléversée (round-trip fidèle).
                photoDisplayUrl: this.detail.photoDisplayUrl || '', // Aperçu <photo-dropzone> uniquement.
                // La fiche ne renvoie que les NOMS des matières qualifiées (voir TeacherProfileDto) :
                // on retrouve leurs identifiants dans le référentiel `subjects` déjà chargé.
                subjectIds: this.subjects.filter((s) => this.detail.subjects.includes(s.name)).map((s) => s.id),
                rowVersion: this.detail.rowVersion
            };
            this.teacherEditErrors = {};
            this.photoUploadError = null;
        },

        closeEditTeacher() {
            this.editingTeacher = null;
            this.teacherEditErrors = {};
            this.photoUploadError = null;
        },

        /**
         * Feature B — dépôt/retrait de la photo depuis la fiche déjà créée : appelle IMMÉDIATEMENT
         * PUT /teachers/{id}/photo (commande dédiée), même raisonnement que students.js.uploadStudentPhoto.
         */
        async uploadTeacherPhoto(photoBase64) {
            if (!this.editingTeacher || !this.detail) return;

            this.photoUploadError = null;
            try {
                const result = await window.api.put(`/teachers/${this.detail.id}/photo`, {
                    photoData: photoBase64,
                    rowVersion: this.editingTeacher.rowVersion
                });
                this.editingTeacher.photoDisplayUrl = result.photoDisplayUrl || '';
                this.editingTeacher.rowVersion = result.rowVersion;
                await this.refreshTeacherDetail();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.photoUploadError = 'Cette fiche vient d\'être modifiée par un autre utilisateur. Elle a été rafraîchie — réessayez.';
                    await this.refreshTeacherDetail();
                    this.editingTeacher.rowVersion = this.detail.rowVersion;
                } else {
                    this.photoUploadError = (err && err.message) || 'Erreur lors de l\'envoi de la photo.';
                }
            }
        },

        async submitEditTeacher() {
            if (!this.editingTeacher || !this.detail) return;

            this.isSavingTeacherEdit = true;
            this.teacherEditErrors = {};
            try {
                await window.api.put(`/teachers/${this.detail.id}`, this.editingTeacher);
                this.closeEditTeacher();
                await this.refreshTeacherDetail();
                await this.loadTeachers();
                this.showTeacherEditedDialog = true;
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.teacherEditErrors = { global: 'Cette fiche vient d\'être modifiée par un autre utilisateur. Elle a été rafraîchie — vérifiez les valeurs puis réessayez.' };
                    await this.refreshTeacherDetail();
                } else {
                    this.teacherEditErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');
                }
            } finally {
                this.isSavingTeacherEdit = false;
            }
        },

        // ------------------------------------------------------------ Supprimer la fiche

        openDeleteTeacher() {
            if (!this.detail) return;
            this.deletingTeacherRecord = { id: this.detail.id, fullName: this.detail.fullName, rowVersion: this.detail.rowVersion };
            this.deleteTeacherRecordError = null;
        },

        closeDeleteTeacher() {
            this.deletingTeacherRecord = null;
            this.deleteTeacherRecordError = null;
        },

        async confirmDeleteTeacher() {
            if (!this.deletingTeacherRecord) return;

            this.isDeletingTeacherRecord = true;
            this.deleteTeacherRecordError = null;
            try {
                await window.api.delete(`/teachers/${this.deletingTeacherRecord.id}?rowVersion=${this.deletingTeacherRecord.rowVersion}`);
                this.deletingTeacherRecord = null;
                this.closeDetail();
                this.page = 1;
                await this.loadTeachers();
                this.showTeacherDeletedDialog = true;
            } catch (err) {
                if (err.code === 'BUSINESS_RULE_VIOLATION') {
                    // Une attribution classe/matière/année existe déjà (DeleteTeacherCommandHandler) :
                    // le message serveur est déjà explicite, on l'affiche tel quel.
                    this.deleteTeacherRecordError = err.message;
                } else if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteTeacherRecordError = 'Cette fiche vient d\'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.';
                    await this.refreshTeacherDetail();
                } else {
                    this.deleteTeacherRecordError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeletingTeacherRecord = false;
            }
        },

        /** Affectations regroupées par année scolaire (l'historique), année active en tête. */
        assignmentsByYear() {
            if (!this.detail || !this.detail.assignments) return [];
            const groups = {};
            for (const a of this.detail.assignments) {
                if (!groups[a.schoolYearId]) {
                    groups[a.schoolYearId] = { label: a.schoolYearLabel, isActive: a.isActiveSchoolYear, items: [] };
                }
                groups[a.schoolYearId].items.push(a);
            }
            return Object.values(groups).sort((x, y) => (y.isActive ? 1 : 0) - (x.isActive ? 1 : 0));
        },

        async submitAssign() {
            this.assignSubmitting = true;
            this.assignError = null;
            this.assignSuccess = false;
            try {
                await window.api.post(`/teachers/${this.detail.id}/assignments`, {
                    classroomId: this.assignForm.classroomId,
                    subjectId: this.assignForm.subjectId
                });
                // Recharge la fiche pour refléter la nouvelle affectation.
                const id = this.detail.id;
                this.assignForm = { classroomId: '', subjectId: '' };
                this.detail = await window.api.get(`/teachers/${id}`);
                this.assignSuccess = true; // formulaire déjà vide et prêt pour l'attribution suivante
            } catch (err) {
                this.assignError = err.message || "Erreur lors de l'attribution.";
            } finally {
                this.assignSubmitting = false;
            }
        },

        deletingAssignmentId: null,
        isDeletingAssignment: false,
        deleteAssignmentError: null,

        openDeleteAssignment(assignmentId) {
            this.deletingAssignmentId = assignmentId;
            this.deleteAssignmentError = null;
        },

        closeDeleteAssignment() {
            this.deletingAssignmentId = null;
        },

        async confirmDeleteAssignment() {
            if (!this.deletingAssignmentId) return;
            this.isDeletingAssignment = true;
            this.deleteAssignmentError = null;
            try {
                await window.api.delete(`/teachers/${this.detail.id}/assignments/${this.deletingAssignmentId}`);
                // Recharge la fiche pour refléter la suppression
                const id = this.detail.id;
                this.detail = await window.api.get(`/teachers/${id}`);
                this.closeDeleteAssignment();
            } catch (err) {
                this.deleteAssignmentError = err.message || "Erreur lors de la suppression de l'affectation.";
            } finally {
                this.isDeletingAssignment = false;
            }
        },

        statusLabel(status) {
            const labels = { Active: 'Actif', Suspended: 'Suspendu', Blocked: 'Bloqué' };
            return labels[status] || status;
        },

        statusClass(status) {
            if (status === 'Active') return 'bg-success-bg text-success';
            if (status === 'Suspended') return 'bg-warning-bg text-warning';
            return 'bg-danger-bg text-danger';
        },

        initials(name) {
            return (name || '').split(' ').filter(Boolean).slice(0, 2).map((p) => p[0]).join('').toUpperCase();
        },

        // ------------------------------------------------------------ Emploi du Temps

        async loadSchedule() {
            this.scheduleSlots = [];
            
            if (this.scheduleViewMode === 'teacher') {
                if (!this.selectedScheduleTeacherId) return;
                this.isLoadingSchedule = true;
                try {
                    this.scheduleSlots = await window.api.get(`/schedules/teacher/${this.selectedScheduleTeacherId}`);
                } catch (err) {
                    this.error = err.message || 'Erreur lors du chargement de l\'emploi du temps de l\'enseignant.';
                } finally {
                    this.isLoadingSchedule = false;
                }
            } else if (this.scheduleViewMode === 'classroom') {
                if (!this.selectedScheduleClassroomId) return;
                this.isLoadingSchedule = true;
                try {
                    this.scheduleSlots = await window.api.get(`/schedules/classroom/${this.selectedScheduleClassroomId}`);
                } catch (err) {
                    this.error = err.message || 'Erreur lors du chargement de l\'emploi du temps de la classe.';
                } finally {
                    this.isLoadingSchedule = false;
                }
            }
        },

        openCreateScheduleSlot() {
            this.createScheduleError = null;
            this.scheduleForm = {
                teacherId: this.scheduleViewMode === 'teacher' ? this.selectedScheduleTeacherId : '',
                classroomId: this.scheduleViewMode === 'classroom' ? this.selectedScheduleClassroomId : '',
                subjectId: '',
                dayOfWeek: 1,
                startTime: '08:00',
                endTime: '09:00',
                roomNumber: ''
            };
            this.creatingScheduleSlot = true;
        },

        closeCreateScheduleSlot() {
            this.creatingScheduleSlot = false;
        },

        async submitCreateScheduleSlot() {
            this.createScheduleError = null;
            this.isSubmittingSchedule = true;
            try {
                await window.api.post('/schedules', this.scheduleForm);
                this.closeCreateScheduleSlot();
                await this.loadSchedule();
            } catch (err) {
                this.createScheduleError = err.errors?.global?.[0] || err.message || 'Erreur lors de l\'enregistrement.';
            } finally {
                this.isSubmittingSchedule = false;
            }
        },

        openDeleteScheduleSlot(slot) {
            this.deleteScheduleError = null;
            this.deletingScheduleSlot = slot;
            this.isDeletingSchedule = false;
        },

        closeDeleteScheduleSlot() {
            this.deletingScheduleSlot = null;
        },

        async confirmDeleteScheduleSlot() {
            this.isDeletingSchedule = true;
            this.deleteScheduleError = null;
            try {
                await window.api.delete(`/schedules/${this.deletingScheduleSlot.id}`);
                this.closeDeleteScheduleSlot();
                await this.loadSchedule();
            } catch (err) {
                this.deleteScheduleError = err.message || 'Erreur lors de la suppression du créneau.';
            } finally {
                this.isDeletingSchedule = false;
            }
        },

        formatTime(timeSpanString) {
            if (!timeSpanString) return '';
            // TimeSpan from backend looks like "08:00:00"
            const parts = timeSpanString.split(':');
            if (parts.length >= 2) {
                return `${parts[0]}h${parts[1] !== '00' ? parts[1] : ''}`;
            }
            return timeSpanString;
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            return new Date(dateStr + 'T00:00:00').toLocaleDateString('fr-FR');
        }
    }));
});
