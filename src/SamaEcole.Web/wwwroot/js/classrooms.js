document.addEventListener('alpine:init', () => {
    Alpine.data('classroomsView', () => ({
        classrooms: [],
        isLoading: false,
        error: null,

        // Recherche + filtre niveau + tri (Volume 5 §6 : tri, recherche, filtres sur toutes les listes)
        search: '',
        levelFilter: '',
        sortKey: 'name',
        sortDir: 'asc',

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

        toggleSort(key) {
            if (this.sortKey === key) {
                this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
            } else {
                this.sortKey = key;
                this.sortDir = 'asc';
            }
        },

        /** Recherche (nom/niveau) + filtre niveau + tri, appliqués à l'affichage sans toucher this.classrooms. */
        visibleClassrooms() {
            const q = this.search.trim().toLowerCase();
            let rows = this.classrooms.filter((c) =>
                (!q || c.name.toLowerCase().includes(q) || c.level.toLowerCase().includes(q)) &&
                (!this.levelFilter || c.level === this.levelFilter));

            const dir = this.sortDir === 'asc' ? 1 : -1;
            rows = [...rows].sort((a, b) => {
                const va = a[this.sortKey];
                const vb = b[this.sortKey];
                if (typeof va === 'number' && typeof vb === 'number') return (va - vb) * dir;
                return String(va).localeCompare(String(vb)) * dir;
            });
            return rows;
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
