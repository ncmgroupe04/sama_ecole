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
        
        // Slide-over — fiche élève (lecture seule)
        detailStudent: null,

        // Slide-over state
        isCreateOpen: false,
        isSubmitting: false,
        newStudent: {
            fullName: '',
            birthDate: '',
            gender: 'M',
            classroomId: '', // Must be UUID
            guardianName: '',
            guardianPhone: ''
        },
        createErrors: {},

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
         * Ouvre la fiche élève (bouton « Actions » de la liste). Aucun appel API : la ligne du tableau
         * porte déjà tous les champs stockés sur l'élève (StudentListItem), il n'y a rien de plus à
         * charger tant que le ticket JGK-D02 (fiche complète — historique scolaire, notes, paiements)
         * n'est pas construit.
         */
        openDetail(student) {
            this.detailStudent = student;
        },

        closeDetail() {
            this.detailStudent = null;
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                const created = await window.api.post('/students', this.newStudent);
                
                // Fermer la modale et réinitialiser
                this.isCreateOpen = false;
                this.newStudent = { fullName: '', birthDate: '', gender: 'M', classroomId: '', guardianName: '', guardianPhone: '' };
                
                // Rafraîchir la liste
                this.page = 1;
                await this.loadStudents();
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
        }
    }));
});
