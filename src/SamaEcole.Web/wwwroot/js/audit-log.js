/**
 * Onglet Journal d'audit (Paramètres, ticket JGK-H01) — lecture seule : les entrées sont écrites
 * automatiquement par AuditLoggingBehavior, aucune action de saisie n'a lieu ici (voir
 * docs/Volume_7_Security.md §7 : « consultable mais jamais modifiable, y compris par un administrateur »).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('auditLogView', () => ({
        entries: [],
        isLoading: false,
        error: null,

        isDirecteur: window.auth.role === 'Directeur',

        page: 1,
        pageSize: 20,
        totalCount: 0,

        moduleFilter: '',
        successFilter: '', // '' = tous, 'true' = succès, 'false' = échecs

        init() {
            if (this.isDirecteur) this.load();
        },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                if (this.moduleFilter) params.set('module', this.moduleFilter);
                if (this.successFilter) params.set('success', this.successFilter);

                const data = await window.api.get(`/audit-logs?${params.toString()}`);
                this.entries = data.items || [];
                this.totalCount = data.totalCount || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du journal.');
            } finally {
                this.isLoading = false;
            }
        },

        applyFilters() {
            this.page = 1;
            this.load();
        },

        resetFilters() {
            this.moduleFilter = '';
            this.successFilter = '';
            this.applyFilters();
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR') + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        }
    }));
});
