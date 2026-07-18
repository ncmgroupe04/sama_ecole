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
            return { Confirmed: 'Confirmée', Pending: 'En attente', Cancelled: 'Annulée' }[status] || status;
        },

        /** Classe de pastille partagée (input.css) selon le statut d'inscription. */
        enrollmentStatusBadge(status) {
            return {
                Confirmed: 'status-badge-success',
                Pending: 'status-badge-warning',
                Cancelled: 'status-badge-danger'
            }[status] || 'status-badge-neutral';
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
