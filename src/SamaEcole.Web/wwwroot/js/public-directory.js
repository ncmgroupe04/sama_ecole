document.addEventListener('alpine:init', () => {
    Alpine.data('publicDirectoryView', () => ({
        schools: [],
        totalCount: 0,
        page: 1,
        pageSize: 12,
        isLoading: false,
        error: null,

        // Recherche par nom + ville, filtre par cycle — les trois seuls critères que
        // GetPublicSchoolsQuery accepte (voir PublicDirectoryController). `cycle` est une
        // valeur brute de l'énumération ("Primaire", "College", "Lycee"), pas un libellé.
        search: '',
        city: '',
        cycle: '',

        init() {
            this.loadSchools();
        },

        /** Un changement de filtre repart de la page 1 : la page 3 d'une recherche précédente n'a pas de sens ici. */
        applyFilters() {
            this.page = 1;
            this.loadSchools();
        },

        async loadSchools() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                if (this.search.trim()) params.set('search', this.search.trim());
                if (this.city.trim()) params.set('city', this.city.trim());
                if (this.cycle) params.set('cycle', this.cycle);

                const data = await window.api.get(`/public/schools?${params.toString()}`);
                this.schools = data.items || [];
                this.totalCount = data.totalCount || 0;
            } catch (err) {
                this.error = err.message || "Impossible de charger l'annuaire des établissements.";
                this.schools = [];
                this.totalCount = 0;
            } finally {
                this.isLoading = false;
            }
        },

        goToPage(delta) {
            const next = this.page + delta;
            if (next < 1 || next > this.totalPages) return;
            this.page = next;
            this.loadSchools();
        },

        get totalPages() {
            return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
        },

        get rangeStart() {
            return this.totalCount === 0 ? 0 : (this.page - 1) * this.pageSize + 1;
        },

        get rangeEnd() {
            return Math.min(this.page * this.pageSize, this.totalCount);
        }
    }));
});
