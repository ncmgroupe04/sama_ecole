/**
 * Tableau de bord financier (ticket JGK-F04) — /finance/dashboard : encaissé jour/mois/année, solde
 * dû, taux de recouvrement sur l'année scolaire active, et les derniers versements encaissés à la
 * caisse. Lecture seule : aucune action de saisie n'a lieu ici (l'encaissement reste l'écran Caisse,
 * JGK-F02) — seule la recherche dans les derniers paiements est interactive.
 */
document.addEventListener('alpine:init', () => {
    /**
     * JGK-R01 — Vue d'ensemble analytique (effectifs de l'année active, enseignants actifs, taux de
     * présence du mois, abonnement). Réservée au Directeur et au Super Admin : confort d'affichage, la
     * garde réelle est ReportsController ([Authorize(Roles = "Directeur,SuperAdmin")]) + la RLS.
     */
    Alpine.data('mainDashboard', () => ({
        analyticsRole: window.auth.role === 'Directeur' || window.auth.role === 'SuperAdmin',
        financeRole: window.auth.role === 'Directeur' || window.auth.role === 'Finance',

        isAnalyticsLoading: false,
        analyticsError: null,
        analyticsData: null,

        isFinanceLoading: false,
        financeError: null,
        financeData: null,

        // Consommé par le partiel _ErrorBanner partagé (x-show="error") : jamais renseigné en
        // pratique ici (chaque chargement a son propre *Error ci-dessus), mais sans cette
        // déclaration Alpine évalue "error" comme une référence indéfinie à chaque rendu.
        error: null,

        search: '',
        activeTab: 'synth', // 'synth', 'expenses'

        // Aperçu PDF (reçu) — état + méthodes étalés depuis le moteur partagé (wwwroot/js/pdf-preview.js).
        ...window.pdfPreview.state(),

        async init() {
            if (this.analyticsRole) this.loadAnalytics();
            if (this.financeRole) this.loadFinance();
        },

        async loadAnalytics() {
            this.isAnalyticsLoading = true;
            this.analyticsError = null;
            try {
                this.analyticsData = await window.api.get('/reports/dashboard');
            } catch (err) {
                this.analyticsError = window.api.toMessage(err, 'Erreur lors du chargement analytique.');
            } finally {
                this.isAnalyticsLoading = false;
            }
        },

        // Il y avait ici un `loadSurveillant()` appelant `/reports/surveillant-dashboard` — route qui
        // N'A JAMAIS EXISTÉ côté serveur (404 systématique, vérifié le 02/09/2026). Son `catch`
        // fabriquait des chiffres EN DUR (« 12 absents, 5 retards, 45 enseignants présents ») et les
        // posait dans `surveillantData`, présenté comme le résultat de l'appel.
        //
        // Rien n'affichait ces données : ni `surveillantData`, ni `surveillantError`, ni le rôle
        // Surveillant n'apparaissaient dans Views/Dashboard/Index.cshtml. Le bloc entier était donc
        // mort, et ne produisait qu'une requête 404 à chaque ouverture de l'écran par un Surveillant.
        //
        // Supprimé plutôt que gardé « en attendant » : des chiffres d'absences inventés, à un
        // brancher-la-vue près de s'afficher comme réels, sont exactement ce qui fait convoquer un
        // parent à tort. Le jour où ce tableau de bord sera livré, il partira d'une vraie route et
        // d'un vrai handler, comme /reports/dashboard et /finance/dashboard juste à côté.

        async loadFinance() {
            this.isFinanceLoading = true;
            this.financeError = null;
            try {
                this.financeData = await window.api.get('/finance/dashboard');
            } catch (err) {
                this.financeError = window.api.toMessage(err, 'Erreur lors du chargement financier.');
            } finally {
                this.isFinanceLoading = false;
            }
        },

        // --- ANALYTICS HELPERS ---
        formatPercent(rate) {
            if (rate === null || rate === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 }).format(rate);
        },

        subscriptionValue() {
            const sub = this.analyticsData && this.analyticsData.subscription;
            if (!sub) return '—';
            if (sub.daysRemaining === null || sub.daysRemaining === undefined) return 'En attente';
            if (sub.daysRemaining < 0) return 'Expiré';
            return `${sub.daysRemaining} j`;
        },

        subscriptionStatusLabel() {
            const sub = this.analyticsData && this.analyticsData.subscription;
            if (!sub) return '';
            const statusLabels = {
                AwaitingPayment: 'En attente de paiement', Active: 'Actif',
                Suspended: 'Suspendu', ReadOnly: 'Lecture seule'
            };
            return `${sub.plan} · ${statusLabels[sub.status] || sub.status}`;
        },

        boysFraction() {
            if (!this.analyticsData || !this.analyticsData.enrollments.total) return 0;
            return (this.analyticsData.enrollments.boys / this.analyticsData.enrollments.total) * 100;
        },

        girlsFraction() {
            if (!this.analyticsData || !this.analyticsData.enrollments.total) return 0;
            return (this.analyticsData.enrollments.girls / this.analyticsData.enrollments.total) * 100;
        },

        // --- FINANCE HELPERS ---
        filteredPayments() {
            if (!this.financeData) return [];
            const q = this.search.trim().toLowerCase();
            if (!q) return this.financeData.recentPayments;
            return this.financeData.recentPayments.filter((p) =>
                p.matricule.toLowerCase().includes(q) ||
                p.studentFullName.toLowerCase().includes(q) ||
                p.receiptNumber.toLowerCase().includes(q));
        },

        resetSearch() {
            this.search = '';
        },

        initials(fullName) {
            return (fullName || '')
                .split(' ')
                .filter(Boolean)
                .slice(0, 2)
                .map((part) => part[0])
                .join('')
                .toUpperCase();
        },

        methodLabel(method) {
            const labels = { Cash: 'Espèces', Cheque: 'Chèque', Transfer: 'Virement', MobileMoney: 'Mobile Money' };
            return labels[method] || method;
        },

        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return window.formatFCFA(amount);
        },

        pct(part, total) {
            if (!total) return '0 %';
            return new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 })
                .format(part / total);
        },

        pctVal(part, total) {
            if (!total) return 0;
            return Math.min(100, Math.round((part / total) * 100));
        },

        getCategoryColor(index) {
            const colors = ['#F59E0B', '#EF4444', '#8B5CF6', '#10B981', '#3B82F6', '#EC4899', '#6366F1'];
            return colors[index % colors.length];
        },

        /**
         * Tableau de longueur FIXE (MAX_DISBURSEMENT_SEGMENTS), jamais parcouru par un `<template
         * x-for>` dans le SVG appelant : le contenu d'un <template> est TOUJOURS analysé en namespace
         * HTML, y compris quand ce <template> est lui-même un enfant de <svg> — un <circle> qui en
         * sortirait ne serait donc pas un SVGCircleElement mais un élément inconnu, invisible sans la
         * moindre erreur visible à l'écran (le donut restait un simple anneau gris, sans le moindre
         * signal d'échec). D'où un nombre FIXE de <circle> écrits en dur dans la vue (Index.cshtml),
         * jamais une boucle — même correctif que paymentMethodSegments() (financial-report.js).
         */
        disbursementSegments() {
            const MAX_DISBURSEMENT_SEGMENTS = 7; // longueur de la palette getCategoryColor()
            const empty = { fraction: 0, offset: 0, color: '#E5E7EB', name: '', amount: 0 };
            if (!this.financeData || !this.financeData.disbursementsByCategory || !this.financeData.totalDisbursements) {
                return Array(MAX_DISBURSEMENT_SEGMENTS).fill(empty);
            }
            let currentOffset = 0;
            const segments = this.financeData.disbursementsByCategory.map((cat, index) => {
                const fraction = (cat.amount / this.financeData.totalDisbursements) * 100;
                const segment = {
                    fraction: fraction,
                    offset: currentOffset,
                    color: this.getCategoryColor(index),
                    name: cat.category,
                    amount: cat.amount
                };
                currentOffset -= fraction; // subtract because offset is inverted on SVG
                return segment;
            });
            while (segments.length < MAX_DISBURSEMENT_SEGMENTS) segments.push(empty);
            return segments;
        },

        barWidth(amount) {
            if (!this.financeData) return 2;
            const max = Math.max(this.financeData.collectedToday, this.financeData.collectedThisMonth, this.financeData.collectedThisYear, 1);
            return Math.max(2, Math.round((amount / max) * 100));
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        },

        /** Ouvre la modale de prévisualisation PDF pour un reçu de paiement (tableau des derniers versements). */
        async previewReceipt(paymentId, receiptNumber) {
            await this.openPdfPreview(
                `/api/v1/finance/payments/${paymentId}/receipt/pdf`,
                `Reçu officiel n° ${receiptNumber}`,
                `Recu-${receiptNumber}.pdf`
            );
        },

        async downloadDailyCashRegisterPdf(dateStr) {
            let date = dateStr;
            if (!date) {
                const today = new Date();
                const yyyy = today.getFullYear();
                const mm = String(today.getMonth() + 1).padStart(2, '0');
                const dd = String(today.getDate()).padStart(2, '0');
                date = `${yyyy}-${mm}-${dd}`;
            }

            // Le journal de caisse s'ouvre dans la modale d'aperçu partagée (pdf-preview.js) :
            // l'utilisateur le relit puis imprime ou télécharge depuis l'en-tête de la modale.
            await this.openPdfPreview(
                `/api/v1/finance/daily-cash-register/pdf?date=${date}`,
                'Journal de caisse',
                `Journal_Caisse_${date}.pdf`
            );
        }
    }));
});
