/**
 * Console Super Admin — Établissements (refonte UI/UX). Consomme le vrai GET /api/v1/schools
 * (SchoolsController, ticket JGK-B01, [Authorize(Roles = SuperAdmin)]) : liste complète, filtrage et
 * recherche instantanée côté client (la liste des écoles reste de taille modeste — pas besoin de
 * pagination serveur pour l'instant, contrairement au journal d'audit).
 *
 * Bouton « Infiltrer » : POST /admin/platform/schools/{schoolId}/impersonate
 * (ImpersonateSchoolCommand) renvoie un jeton d'impersonation à courte durée de vie portant
 * l'identité du Directeur de l'école ciblée. auth.enterImpersonation() bascule la session dessus et
 * on redirige vers le tableau de bord tenant — _Layout.cshtml affiche alors le bandeau permettant d'en
 * sortir (wwwroot/js/auth.js, impersonationBanner).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminSchools', () => ({
        schools: [],
        isLoading: false,
        error: null,
        searchQuery: '',
        statusFilter: 'All',

        impersonateTarget: null, // { id, name }
        isImpersonating: false,
        impersonateError: null,

        statusTarget: null, // { id, name, currentStatus, newStatus }
        statusReason: '',
        isChangingStatus: false,
        statusError: null,

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.schools = await window.api.get('/schools');
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des établissements.');
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
        },

        // ------------------------------------------------------------ Infiltrer

        openImpersonateConfirm(school) {
            this.impersonateTarget = { id: school.id, name: school.name };
            this.impersonateError = null;
        },

        closeImpersonateConfirm() {
            this.impersonateTarget = null;
        },

        async confirmImpersonate() {
            if (!this.impersonateTarget) return;

            this.isImpersonating = true;
            this.impersonateError = null;
            try {
                const tokens = await window.api.post(`/admin/platform/schools/${this.impersonateTarget.id}/impersonate`);
                window.auth.enterImpersonation(tokens);
                window.location.assign('/tableau-de-bord');
            } catch (err) {
                this.impersonateError = window.api.toMessage(err, "Erreur lors de l'infiltration.");
                this.isImpersonating = false;
            }
        },

        // ------------------------------------------------------------ Changement de statut

        openStatusConfirm(school, newStatus) {
            this.statusTarget = { id: school.id, name: school.name, newStatus };
            this.statusReason = '';
            this.statusError = null;
        },

        closeStatusConfirm() {
            this.statusTarget = null;
        },

        statusModalTitle() {
            const labels = { Active: 'Réactiver l\'établissement', Suspended: 'Suspendre l\'établissement', Blocked: 'Bloquer l\'établissement' };
            return this.statusTarget ? (labels[this.statusTarget.newStatus] || 'Changer le statut') : '';
        },

        async confirmStatusChange() {
            if (!this.statusTarget) return;

            if (!this.statusReason.trim()) {
                this.statusError = 'Le motif est obligatoire.';
                return;
            }

            this.isChangingStatus = true;
            this.statusError = null;
            try {
                await window.api.patch(`/schools/${this.statusTarget.id}/status`, {
                    status: this.statusTarget.newStatus,
                    reason: this.statusReason.trim()
                });
                this.closeStatusConfirm();
                await this.load();
            } catch (err) {
                this.statusError = window.api.toMessage(err, 'Erreur lors du changement de statut.');
            } finally {
                this.isChangingStatus = false;
            }
        }
    }));
});
