document.addEventListener('alpine:init', () => {
    Alpine.data('classroomsView', () => ({
        classrooms: [],
        isLoading: false,
        error: null,
        
        // Modal State
        isCreateOpen: false,
        isSubmitting: false,
        newClassroom: {
            name: '',
            level: 'Primaire',
            capacity: 30
        },
        createErrors: {},

        init() {
            this.loadClassrooms();
        },

        async loadClassrooms() {
            this.isLoading = true;
            this.error = null;
            try {
                // The API endpoint /classrooms returns a list of classrooms
                const data = await window.api.get('/classrooms');
                // Assume the response is either an array or has an items property
                this.classrooms = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                this.error = err.message || "Erreur lors du chargement des classes.";
            } finally {
                this.isLoading = false;
            }
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                await window.api.post('/classrooms', this.newClassroom);
                
                this.isCreateOpen = false;
                this.newClassroom = { name: '', level: 'Primaire', capacity: 30 };
                await this.loadClassrooms();
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, "Erreur lors de la création.");
            } finally {
                this.isSubmitting = false;
            }
        },

        // Utilities
        getTotalCapacity() {
            return this.classrooms.reduce((sum, c) => sum + (c.capacity || 0), 0);
        }
    }));
});
