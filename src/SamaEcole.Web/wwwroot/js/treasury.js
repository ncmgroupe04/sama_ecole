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
                this.error = window.api.toMessage(err, 'Erreur lors du chargement de la trésorerie.');
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
            // Classes du design system (.status-badge-*) plutôt qu'une paire bg/text écrite ici : la
            // pastille de la Trésorerie doit être la même que celle de la Caisse ou des Frais, sans
            // qu'un écran ait à connaître les codes couleur.
            return kind === 'Encaissement' ? 'status-badge-success' : 'status-badge-danger';
        },

        /**
         * Part d'un poste dans le total de SA colonne (encaissé ou décaissé), en pourcentage entier
         * 0-100 — la largeur de la jauge, pas un libellé.
         *
         * Le total est le dénominateur, jamais le plus gros poste : « 60 % des encaissements en
         * espèces » est ce qui intéresse le directeur ; « deux fois plus que le mobile money » ne se
         * lit pas sur une barre. Un total nul rend 0 plutôt que NaN, qui produirait `width: NaN%` —
         * une barre pleine dans certains navigateurs.
         */
        sharePct(amount, total) {
            const t = Number(total) || 0;
            if (t <= 0) return 0;
            return Math.min(100, Math.max(0, Math.round((Number(amount) || 0) / t * 100)));
        },

        /** Même part, en texte, pour l'accompagnement du libellé (la couleur seule ne suffit pas). */
        shareLabel(amount, total) {
            return `${this.sharePct(amount, total)} %`;
        },

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatAmount(amount) {
            return window.formatFCFA(amount);
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
