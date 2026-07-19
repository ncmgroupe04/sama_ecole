document.addEventListener('alpine:init', () => {
    Alpine.data('studentsView', () => ({
        students: [],
        classrooms: [],
        totalCount: 0,
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
        
        // Fiche élève (JGK-D02) — detailStudent porte la ligne de liste (affichage immédiat de
        // l'identité), studentDetail la fiche complète chargée depuis GET /students/{id}
        // (historique scolaire, notes, paiements).
        detailStudent: null,
        studentDetail: null,
        isLoadingDetails: false,
        detailError: null,
        detailTab: 'history',

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
            guardianName: '',
            guardianPhone: ''
        },
        createErrors: {},

        // Confirmation « Élève ajouté » affichée après un enregistrement réussi.
        showAddedDialog: false,
        addedStudentName: '',

        // Corriger/archiver une fiche, et gérer le cycle de vie d'une inscription (annuler, déclarer
        // un abandon/transfert) sont réservés au Directeur et au Secrétariat côté serveur
        // (StudentsController.ManageRoles, EnrollmentsController.EnrollmentWriters) — confort d'affichage.
        canManageStudent: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        // Édition de la fiche (modale). Sourcée depuis studentDetail.identity (fraîchement chargée,
        // RowVersion inclus) plutôt que la ligne de liste `detailStudent`, qui peut être périmée et ne
        // porte pas le jeton de concurrence — le bouton n'est donc proposé qu'une fois studentDetail chargé.
        editingStudent: null, // { fullName, birthDate, birthPlace, gender, classroomId, photoUrl, guardianName, guardianPhone, rowVersion }
        isSavingStudentEdit: false,
        studentEditErrors: {},
        showStudentEditedDialog: false,

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
        changingEnrollmentStatus: null, // { enrollmentId, schoolYearLabel, rowVersion, newStatus }
        isChangingEnrollmentStatus: false,
        changeEnrollmentStatusError: null,

        // Initialisation
        init() {
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

                const data = await window.api.get(`/students?${params.toString()}`);
                this.students = data.items || [];
                this.totalCount = data.totalCount || 0;
            } catch (err) {
                this.error = err.message || "Erreur lors du chargement des élèves.";
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
            this.applyFilters();
        },

        /**
         * Ouvre la fiche élève (JGK-D02). La ligne de liste (StudentListItem) affiche l'identité
         * immédiatement ; la fiche complète — historique scolaire, notes par trimestre, paiements —
         * est chargée depuis GET /students/{id}. L'onglet repart toujours sur « Historique ».
         */
        async openDetail(student) {
            this.detailStudent = student;
            this.studentDetail = null;
            this.detailError = null;
            this.detailTab = 'history';
            this.isLoadingDetails = true;
            try {
                this.studentDetail = await window.api.get(`/students/${student.id}`);
            } catch (err) {
                this.detailError = err.message || "Impossible de charger la fiche complète de l'élève.";
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
                photoUrl: identity.photoUrl || '',
                guardianName: identity.guardianName || '',
                guardianPhone: identity.guardianPhone || '',
                rowVersion: identity.rowVersion
            };
            this.studentEditErrors = {};
        },

        closeEditStudent() {
            this.editingStudent = null;
            this.studentEditErrors = {};
        },

        async submitEditStudent() {
            if (!this.editingStudent || !this.detailStudent) return;

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
            this.changeEnrollmentStatusError = null;
        },

        closeChangeEnrollmentStatus() {
            this.changingEnrollmentStatus = null;
            this.changeEnrollmentStatusError = null;
        },

        async submitChangeEnrollmentStatus() {
            if (!this.changingEnrollmentStatus) return;

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
                this.newStudent = { fullName: '', birthDate: '', birthPlace: '', gender: 'M', classroomId: '', photoUrl: '', guardianName: '', guardianPhone: '' };

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
        
        // Utilitaires de présentation
        formatDate(dateStr) {
            if (!dateStr) return '';
            const date = new Date(dateStr);
            return date.toLocaleDateString('fr-FR');
        },
        
        getInitials(name) {
            if (!name) return '??';
            return name.split(' ').map(n => n[0]).join('').substring(0, 2).toUpperCase();
        },

        // ----- Fiche élève (JGK-D02) : utilitaires de présentation -----

        /** Montant en FCFA, séparateurs de milliers français, sans décimale (la caisse travaille en entiers). */
        formatAmount(amount) {
            if (amount === null || amount === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount) + ' FCFA';
        },

        /**
         * Note lisible : au plus une décimale, sans zéro inutile (17.0 → « 17 », 14.5 → « 14,5 »),
         * même convention que le bulletin (DDS §8). Renvoie « — » si la note est absente.
         */
        formatGrade(value) {
            if (value === null || value === undefined) return '—';
            return (Math.round(value * 10) / 10).toLocaleString('fr-FR');
        },

        /** Moyenne suffixée du barème de l'école (14,5/20 ou 7,2/10) — jamais « /20 » supposé. */
        formatAverage(value) {
            if (value === null || value === undefined) return '—';
            const scale = this.studentDetail?.gradingScale ?? 20;
            return `${this.formatGrade(value)}/${scale}`;
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
        }
    }));
});
