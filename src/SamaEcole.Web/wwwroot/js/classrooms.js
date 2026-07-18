document.addEventListener('alpine:init', () => {
    // Ordre pédagogique des cycles ; un niveau hors nomenclature passe en fin, par ordre alphabétique.
    const LEVEL_ORDER = ['Crèche', 'Maternelle', 'Primaire', 'Collège', 'Lycée'];

    Alpine.data('classroomsView', () => ({
        classrooms: [],
        isLoading: false,
        error: null,

        // Recherche + filtre niveau (Volume 5 §6 : recherche et filtres sur toutes les listes)
        search: '',
        levelFilter: '',

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

        /**
         * Une section par cycle (même lecture que l'écran Matières) : le niveau se voit au premier
         * regard dans l'en-tête de section, plus besoin de le répéter devant chaque classe.
         */
        get groups() {
            const q = this.search.trim().toLowerCase();
            const visible = this.classrooms.filter((c) =>
                (!q || c.name.toLowerCase().includes(q) || c.level.toLowerCase().includes(q)) &&
                (!this.levelFilter || c.level === this.levelFilter));

            const byLevel = new Map();
            for (const classroom of visible) {
                if (!byLevel.has(classroom.level)) byLevel.set(classroom.level, []);
                byLevel.get(classroom.level).push(classroom);
            }

            return Array.from(byLevel, ([level, classrooms]) => ({
                level,
                // « numeric: true » pour que 6e < 10e — un tri texte brut mettrait 10e avant 6e.
                classrooms: [...classrooms].sort((a, b) => a.name.localeCompare(b.name, 'fr', { numeric: true })),
                totalCapacity: classrooms.reduce((sum, c) => sum + (c.capacity || 0), 0),
                totalStudents: classrooms.reduce((sum, c) => sum + (c.studentCount || 0), 0)
            })).sort((a, b) => {
                const ia = LEVEL_ORDER.indexOf(a.level);
                const ib = LEVEL_ORDER.indexOf(b.level);
                if (ia !== ib) return (ia === -1 ? LEVEL_ORDER.length : ia) - (ib === -1 ? LEVEL_ORDER.length : ib);
                return a.level.localeCompare(b.level, 'fr');
            });
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
        },

        plural(count, singular, plural) {
            return count + ' ' + (count > 1 ? plural : singular);
        }
    }));
});
