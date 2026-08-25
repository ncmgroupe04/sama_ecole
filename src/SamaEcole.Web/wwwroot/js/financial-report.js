/**
 * JGK-F05 — Rapports financiers (/rapports/financiers). Consolidation des encaissements sur une
 * période, ventilée par cycle, par classe et par mode de paiement, doublée de l'export comptable
 * .xlsx.
 *
 * Les deux boutons interrogent des routes JUMELLES du même contrôleur (GET /finance/reports/revenue
 * et .../revenue/excel) : l'écran et le fichier téléchargé portent donc exactement les mêmes chiffres,
 * sur exactement les mêmes bornes de période (voir GetRevenueConsolidationExcelQueryHandler, qui
 * réutilise la requête d'affichage au lieu de recalculer).
 *
 * Réservé au Directeur et à la Finance, ET aux formules Standard/Premium : la garde réelle est
 * FinancialReportsController ([Authorize] + [RequireFeature(AdvancedFinancialReports)]) — ce que fait
 * `featureGate` en façade n'est qu'un confort d'affichage (voir features.js).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('financialReportView', () => ({
        data: null,
        isLoading: false,
        error: null,
        exporting: false,

        // Verrou de formule porté par CE composant plutôt que par le composant partagé `featureGate` :
        // le bouton d'export appelle exportExcel(), qui écrit `exporting` — il doit vivre dans la même
        // portée Alpine, sans quoi une portée imbriquée masquerait la propriété du parent.
        featureAllowed: false,
        featureLabel: window.features.upgradeLabel('AdvancedFinancialReports'),

        // Période par défaut : du 1er janvier à aujourd'hui — l'API retient l'année civile en cours
        // quand aucune borne n'est fournie, l'écran affiche donc d'emblée la même chose qu'elle.
        from: '',
        to: '',

        init() {
            const today = new Date();
            this.from = this.toIsoDate(new Date(today.getFullYear(), 0, 1));
            this.to = this.toIsoDate(today);

            this.loadFeatures();
            this.load();
        },

        async loadFeatures() {
            await window.features.load();
            this.featureAllowed = window.features.has('AdvancedFinancialReports');
        },

        toIsoDate(d) {
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${d.getFullYear()}-${month}-${day}`;
        },

        async load() {
            if (!this.from || !this.to) return;

            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ from: this.from, to: this.to });
                this.data = await window.api.get(`/finance/reports/revenue?${params.toString()}`);
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement du rapport financier.';
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
            this.from = this.toIsoDate(new Date(today.getFullYear(), 0, 1));
            this.to = this.toIsoDate(today);
            this.applyFilters();
        },

        /**
         * Téléchargement .xlsx — fetch bas niveau plutôt que window.api : la réponse est un binaire,
         * pas du JSON, et le jeton doit voyager en en-tête (il vit dans localStorage, jamais dans un
         * cookie). Même mécanique que l'export du rapport d'assiduité (attendance-report.js).
         */
        async exportExcel() {
            if (!this.from || !this.to || this.exporting) return;

            this.exporting = true;
            this.error = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const params = new URLSearchParams({ from: this.from, to: this.to });
                const response = await fetch(`/api/v1/finance/reports/revenue/excel?${params.toString()}`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });

                if (!response.ok) {
                    this.error = response.status === 403
                        ? "L'export comptable est réservé aux formules Standard et Premium."
                        : "Erreur lors de l'export du rapport financier.";
                    return;
                }

                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = this.exportFileName(response);
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                this.error = err.message || "Erreur lors de l'export du rapport financier.";
            } finally {
                this.exporting = false;
            }
        },

        /** Nom du fichier : Content-Disposition renvoyé par l'API si présent, sinon reconstruit des bornes. */
        exportFileName(response) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
            if (match) return decodeURIComponent(match[1]);

            return `revenus_${this.from.replace(/-/g, '')}_${this.to.replace(/-/g, '')}.xlsx`;
        },

        periodLabel() {
            if (!this.data) return '';
            return `${this.formatDate(this.data.from)} → ${this.formatDate(this.data.to)}`;
        },

        /** Part d'une ligne dans le total encaissé — 0 quand le total l'est aussi (pas de division par zéro). */
        share(amount) {
            const total = this.data ? this.data.totalCollected : 0;
            if (!total) return 0;
            return amount / total;
        },

        // --- GRAPHIQUES — même technique que le tableau de bord financier (dashboard.js) : donut en
        // SVG inline (stroke-dasharray) et barres CSS en pourcentage, pas de librairie de graphes.

        /** Hauteur de barre (%) relative au mois le plus fort de la période — pas au total, pour que
         * la série d'évolution reste lisible même sur une période longue (barWidth() de dashboard.js
         * fait de même pour comparer jour/mois/année). */
        monthBarHeight(amount) {
            if (!this.data || this.data.byMonth.length === 0) return 2;
            const max = Math.max(...this.data.byMonth.map((m) => m.amount), 1);
            return Math.max(2, Math.round((amount / max) * 100));
        },

        /** "2026-01-01" → "Janv. 26", même paire mois-court/année-courte que le badge d'année scolaire. */
        formatMonth(periodStr) {
            const d = new Date(periodStr);
            const label = new Intl.DateTimeFormat('fr-FR', { month: 'short', year: '2-digit' }).format(d);
            return label.charAt(0).toUpperCase() + label.slice(1);
        },

        /** Palette et construction de segments IDENTIQUES à disbursementSegments()/getCategoryColor()
         * de dashboard.js — même astuce de cercle SVG (circonférence 100, stroke-dashoffset cumulé). */
        getCategoryColor(index) {
            const colors = ['#F59E0B', '#EF4444', '#8B5CF6', '#10B981', '#3B82F6', '#EC4899', '#6366F1'];
            return colors[index % colors.length];
        },

        /**
         * Tableau de longueur FIXE (MAX_PAYMENT_SEGMENTS), jamais parcouru par `<template x-for>` dans
         * le SVG appelant : le contenu d'un <template> est TOUJOURS analysé en namespace HTML, y compris
         * quand le <template> est lui-même un enfant de <svg> — un <circle> qui en sort n'est donc pas un
         * SVGCircleElement mais un élément inconnu, invisible sans la moindre erreur visible à l'écran
         * (seule la console révèle "segment is not defined" / "reading 'children'"). D'où un nombre FIXE
         * de <circle> écrits en dur dans la vue plutôt qu'une boucle — même piège que celui déjà présent
         * dans disbursementSegments()/Dashboard/Index.cshtml (onglet Décaissements), non corrigé ici :
         * hors périmètre de ce ticket, signalé séparément.
         */
        paymentMethodSegments() {
            const MAX_PAYMENT_SEGMENTS = 7; // longueur de la palette getCategoryColor()
            const empty = { fraction: 0, offset: 0, color: '#E5E7EB', name: '', amount: 0 };
            if (!this.data || this.data.byPaymentMethod.length === 0 || !this.data.totalCollected) {
                return Array(MAX_PAYMENT_SEGMENTS).fill(empty);
            }
            let currentOffset = 0;
            const segments = this.data.byPaymentMethod.map((item, index) => {
                const fraction = (item.amount / this.data.totalCollected) * 100;
                const segment = { fraction, offset: currentOffset, color: this.getCategoryColor(index), name: item.label, amount: item.amount };
                currentOffset -= fraction; // inversé : le sens de l'arc SVG est opposé à celui de la liste
                return segment;
            });
            while (segments.length < MAX_PAYMENT_SEGMENTS) segments.push(empty);
            return segments;
        },

        /** Même pctVal(part, total) que dashboard.js : pourcentage borné 0-100, jamais de division par zéro. */
        pctVal(part, total) {
            if (!total) return 0;
            return Math.min(100, Math.round((part / total) * 100));
        },

        /** Les 6 classes les plus fortes : la barre est un résumé visuel, le tableau plus bas garde le détail complet. */
        topClassrooms() {
            if (!this.data) return [];
            return this.data.byClassroom.slice(0, 6);
        },

        formatPercent(ratio) {
            return `${((ratio || 0) * 100).toFixed(1)} %`;
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
