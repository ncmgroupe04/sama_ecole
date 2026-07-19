/**
 * Console Super Admin — Établissements (refonte UI/UX). Consomme le vrai GET /api/v1/schools
 * (SchoolsController, ticket JGK-B01, [Authorize(Roles = SuperAdmin)]) : liste complète, filtrage et
 * recherche instantanée côté client (la liste des écoles reste de taille modeste — pas besoin de
 * pagination serveur pour l'instant, contrairement au journal d'audit).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminSchools', () => ({
        schools: [],
        isLoading: false,
        error: null,
        searchQuery: '',
        statusFilter: 'All',

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.schools = await window.api.get('/schools');
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des établissements.';
            } finally {
                this.isLoading = false;
            }
        },

        get filteredSchools() {
            const query = this.searchQuery.trim().toLowerCase();

            return this.schools.filter((school) => {
                const matchesStatus = this.statusFilter === 'All' || school.status === this.statusFilter;
                const matchesQuery = !query || [school.name, school.address, school.phone]
                    .filter(Boolean)
                    .some((field) => field.toLowerCase().includes(query));

                return matchesStatus && matchesQuery;
            });
        },

        countByStatus(status) {
            return this.schools.filter((s) => s.status === status).length;
        },

        statusLabel(status) {
            return { Active: 'Actif', Suspended: 'Suspendu', Blocked: 'Bloqué' }[status] || status;
        },

        statusClasses(status) {
            return {
                Active: 'bg-emerald-500/15 text-emerald-400 ring-1 ring-inset ring-emerald-500/20',
                Suspended: 'bg-amber-500/15 text-amber-400 ring-1 ring-inset ring-amber-500/20',
                Blocked: 'bg-rose-500/15 text-rose-400 ring-1 ring-inset ring-rose-500/20'
            }[status] || 'bg-zinc-800 text-zinc-300';
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleDateString('fr-FR');
        }
    }));
});
