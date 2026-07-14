document.addEventListener('alpine:init', () => {
    Alpine.data('studentsView', () => ({
        students: [],
        classrooms: [],
        totalCount: 0,
        page: 1,
        pageSize: 10,
        isLoading: false,
        error: null,
        
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
                // Request page data
                const data = await window.api.get(`/students?page=${this.page}&pageSize=${this.pageSize}`);
                this.students = data.items || [];
                this.totalCount = data.totalCount || 0;
            } catch (err) {
                this.error = err.message || "Erreur lors du chargement des élèves.";
            } finally {
                this.isLoading = false;
            }
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
