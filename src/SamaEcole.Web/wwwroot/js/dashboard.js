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
    Alpine.data('dashboardAnalytics', () => ({
        canView: window.auth.role === 'Directeur' || window.auth.role === 'SuperAdmin',

        isLoading: false,
        error: null,
        data: null,

        async init() {
            if (this.canView) await this.load();
        },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.data = await window.api.get('/reports/dashboard');
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement du tableau de bord.';
            } finally {
                this.isLoading = false;
            }
        },

        /** Taux de présence : « — » quand aucun appel n'a encore été saisi ce mois (attendanceRate null). */
        formatPercent(rate) {
            if (rate === null || rate === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 }).format(rate);
        },

        /** Valeur principale de la carte Abonnement : jours restants, ou un libellé si pas d'échéance. */
        subscriptionValue() {
            const sub = this.data && this.data.subscription;
            if (!sub) return '—';
            if (sub.daysRemaining === null || sub.daysRemaining === undefined) return 'En attente';
            if (sub.daysRemaining < 0) return 'Expiré';
            return `${sub.daysRemaining} j`;
        },

        /** Statut lisible à côté de la valeur (plan + statut brut de l'abonnement). */
        subscriptionStatusLabel() {
            const sub = this.data && this.data.subscription;
            if (!sub) return '';
            const statusLabels = {
                AwaitingPayment: 'En attente de paiement', Active: 'Actif',
                Suspended: 'Suspendu', ReadOnly: 'Lecture seule'
            };
            return `${sub.plan} · ${statusLabels[sub.status] || sub.status}`;
        },

        /** Pourcentage affiché (ex. « 42 % ») ; « 0 % » sur un total vide plutôt qu'une division par zéro. */
        pct(part, total) {
            if (!total) return '0 %';
            return new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 })
                .format(part / total);
        },

        /** Part EXACTE (non arrondie) des garçons dans l'effectif, en points sur 100 — alimente le
         * cercle du donut (circonférence de 100 avec r = 15.9155), où un arrondi laisserait un espace
         * visible entre les deux segments. */
        boysFraction() {
            if (!this.data || !this.data.enrollments.total) return 0;
            return (this.data.enrollments.boys / this.data.enrollments.total) * 100;
        },

        girlsFraction() {
            if (!this.data || !this.data.enrollments.total) return 0;
            return (this.data.enrollments.girls / this.data.enrollments.total) * 100;
        }
    }));

    Alpine.data('dashboardView', () => ({
        // Seuls le Directeur et la Finance pilotent la trésorerie (règle #4, comme la Caisse). Confort
        // d'affichage : l'API garde (FinanceController.Dashboard, [Authorize(Roles = "Directeur,Finance")]).
        canView: window.auth.role === 'Directeur' || window.auth.role === 'Finance',

        isLoading: false,
        error: null,
        data: null,
        search: '',
        activeTab: 'dual', // 'dual' (côte à côte), 'annual' (bilan annuel), 'monthly' (bilan mensuel)

        // Modale d'aperçu et d'impression du reçu officiel
        showPdfModal: false,
        pdfPreviewUrl: null,
        pdfPreviewTitle: '',
        pdfDownloadName: '',

        async init() {
            if (this.canView) await this.load();
        },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.data = await window.api.get('/finance/dashboard');
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement du tableau de bord.';
            } finally {
                this.isLoading = false;
            }
        },

        filteredPayments() {
            if (!this.data) return [];
            const q = this.search.trim().toLowerCase();
            if (!q) return this.data.recentPayments;
            return this.data.recentPayments.filter((p) =>
                p.matricule.toLowerCase().includes(q) ||
                p.studentFullName.toLowerCase().includes(q) ||
                p.receiptNumber.toLowerCase().includes(q));
        },

        resetSearch() {
            this.search = '';
        },

        /** Initiales pour l'avatar de ligne (ex. « Awa Ndiaye » → « AN »), même idiome que le profil de la sidebar. */
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

        /** FCFA : entiers, séparateur de milliers français. Pas de décimales — la monnaie n'en a pas. */
        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount) + ' FCFA';
        },

        formatPercent(rate) {
            return new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 }).format(rate || 0);
        },

        /** Pourcentage affiché (ex. « 42 % ») ; « 0 % » sur un dénominateur vide plutôt qu'une division par zéro. */
        pct(part, total) {
            if (!total) return '0 %';
            return new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 })
                .format(part / total);
        },

        /** Largeur de barre (0-100) relative au plus grand des trois montants comparés — jour/mois/année
         * partagent donc une même échelle plutôt que chacune sa propre barre pleine. Un minimum de 2 %
         * garde la barre visible (donc cliquable/lisible) même sur un montant nul. */
        barWidth(amount) {
            const max = Math.max(this.data.collectedToday, this.data.collectedThisMonth, this.data.collectedThisYear, 1);
            return Math.max(2, Math.round((amount / max) * 100));
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        },

        /**
         * Ouvre le reçu officiel en PDF dans une modale d'aperçu (avec impression ou téléchargement).
         */
        async previewReceipt(paymentId, receiptNumber) {
            if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                await window.api.refreshOrRedirect();
            }

            const response = await fetch(`/api/v1/finance/payments/${paymentId}/receipt/pdf`, {
                headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                credentials: 'same-origin'
            });
            if (!response.ok) return;

            const blob = await response.blob();
            if (this.pdfPreviewUrl) URL.revokeObjectURL(this.pdfPreviewUrl);
            this.pdfPreviewUrl = URL.createObjectURL(blob);
            this.pdfPreviewTitle = `Reçu officiel n° ${receiptNumber}`;
            this.pdfDownloadName = `Recu-${receiptNumber}.pdf`;
            this.showPdfModal = true;
        },

        closePdfPreview() {
            this.showPdfModal = false;
            if (this.pdfPreviewUrl) {
                URL.revokeObjectURL(this.pdfPreviewUrl);
                this.pdfPreviewUrl = null;
            }
        },

        printPreviewPdf() {
            const iframe = document.getElementById('dash-pdf-preview-frame');
            if (iframe && iframe.contentWindow) iframe.contentWindow.print();
        },

        downloadPreviewPdf() {
            if (!this.pdfPreviewUrl) return;
            const link = document.createElement('a');
            link.href = this.pdfPreviewUrl;
            link.download = this.pdfDownloadName || 'document.pdf';
            document.body.appendChild(link);
            link.click();
            link.remove();
        }
    }));
});
