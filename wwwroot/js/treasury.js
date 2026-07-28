/**
 * Module Comptabilité & Fiscalité — Tableau de bord Trésorerie (/tresorerie). Agrège les encaissements
 * (module Caisse) et les décaissements déjà existants sur une période ; n'introduit aucun nouveau
 * registre de mouvements. Réservé au Directeur et à la Finance (garde réelle : FinanceController).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('treasuryView', () => ({
        data: null,
        isLoading: false,
        error: null,

        // Période par défaut : du 1er du mois courant à aujourd'hui (même convention que le rapport d'assiduité).
        startDate: '',
        endDate: '',

        init() {
            const today = new Date();
            const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
            this.startDate = this.toIsoDate(firstOfMonth);
            this.endDate = this.toIsoDate(today);

            this.load();
        },

        toIsoDate(d) {
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${d.getFullYear()}-${month}-${day}`;
        },

        async load() {
            if (!this.startDate || !this.endDate) return;

            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ startDate: this.startDate, endDate: this.endDate });
                this.data = await window.api.get(`/finance/treasury?${params.toString()}`);
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement de la trésorerie.';
                this.data = null;
            } finally {
                this.isLoading = false;
            }
        },

        applyFilters() {
            this.load();
        },

        resetFilters() {
            const today = new Date();
            const firstOfMonth = new Date(today.getFullYear(), today.getMonth(), 1);
            this.startDate = this.toIsoDate(firstOfMonth);
            this.endDate = this.toIsoDate(today);
            this.applyFilters();
        },

        periodLabel() {
            if (!this.data) return '';
            return `${this.formatDate(this.data.startDate)} → ${this.formatDate(this.data.endDate)}`;
        },

        kindBadgeClass(kind) {
            return kind === 'Encaissement' ? 'bg-success-bg text-success' : 'bg-danger-bg text-danger';
        },

        /** FCFA : entiers, séparateur de milliers français. Pas de décimales — la monnaie n'en a pas. */
        formatAmount(amount) {
            return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount || 0) + ' FCFA';
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            const d = new Date(dateStr);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        }
    }));
});
