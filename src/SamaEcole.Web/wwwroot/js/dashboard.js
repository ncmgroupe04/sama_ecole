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
        
        search: '',
        activeTab: 'synth', // 'synth', 'expenses'

        // Modale PDF
        showPdfModal: false,
        pdfPreviewUrl: null,
        pdfPreviewTitle: '',
        pdfDownloadName: '',
        pdfLoadError: false,

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
                this.analyticsError = err.message || 'Erreur lors du chargement analytique.';
            } finally {
                this.isAnalyticsLoading = false;
            }
        },

        async loadFinance() {
            this.isFinanceLoading = true;
            this.financeError = null;
            try {
                this.financeData = await window.api.get('/finance/dashboard');
            } catch (err) {
                this.financeError = err.message || 'Erreur lors du chargement financier.';
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
            return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount) + ' FCFA';
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

        disbursementSegments() {
            if (!this.financeData || !this.financeData.disbursementsByCategory || !this.financeData.totalDisbursements) return [];
            let currentOffset = 0;
            return this.financeData.disbursementsByCategory.map((cat, index) => {
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

        async previewReceipt(paymentId, receiptNumber) {
            if (window.closeAllModals) {
                window.closeAllModals();
                await new Promise(resolve => setTimeout(resolve, 150));
            }

            if (this.pdfPreviewUrl) {
                URL.revokeObjectURL(this.pdfPreviewUrl);
                this.pdfPreviewUrl = null;
            }
            this.pdfLoadError = false;
            this.pdfPreviewTitle = `Reçu officiel n° ${receiptNumber}`;
            this.pdfDownloadName = `Recu-${receiptNumber}.pdf`;

            try {
                if (!paymentId || paymentId === 'undefined' || paymentId === 'null') {
                    throw new Error("L'identifiant de paiement est invalide (" + paymentId + ").");
                }
                const url = `/api/v1/finance/payments/${paymentId}/receipt/pdf`;
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(url, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) {
                    const errText = await response.text().catch(() => '');
                    throw new Error(`Erreur ${response.status}: Téléchargement du document impossible (${errText || response.statusText}).`);
                }

                const rawBlob = await response.blob();
                if (!rawBlob || rawBlob.size === 0) {
                    throw new Error("Le document PDF reçu est vide (0 octet). Veuillez réessayer.");
                }
                const pdfBlob = new Blob([rawBlob], { type: 'application/pdf' });
                this.pdfPreviewUrl = URL.createObjectURL(pdfBlob);
            } catch (err) {
                console.error("Erreur previewReceipt (Dashboard):", err);
                this.pdfLoadError = true;
            }
            this.showPdfModal = true;
        },

        closePdfPreview() {
            this.showPdfModal = false;
            if (this.pdfPreviewUrl) {
                URL.revokeObjectURL(this.pdfPreviewUrl);
                this.pdfPreviewUrl = null;
            }
            this.pdfLoadError = false;
        },

        printPreviewPdf() {
            const iframe = document.getElementById('dash-pdf-preview-frame');
            if (iframe && iframe.contentWindow) {
                try {
                    iframe.contentWindow.focus();
                    iframe.contentWindow.print();
                } catch {
                    if (this.pdfPreviewUrl) {
                        const win = window.open(this.pdfPreviewUrl, '_blank');
                        if (win) win.print();
                    }
                }
            } else if (this.pdfPreviewUrl) {
                const win = window.open(this.pdfPreviewUrl, '_blank');
                if (win) win.print();
            }
        },

        downloadPreviewPdf() {
            if (!this.pdfPreviewUrl) return;
            const link = document.createElement('a');
            link.href = this.pdfPreviewUrl;
            link.download = this.pdfDownloadName || 'document.pdf';
            document.body.appendChild(link);
            link.click();
            link.remove();
        },

        async downloadDailyCashRegisterPdf(dateStr) {
            try {
                let dateParam = '';
                if (dateStr) {
                    dateParam = `?date=${dateStr}`;
                } else {
                    const today = new Date();
                    const yyyy = today.getFullYear();
                    const mm = String(today.getMonth() + 1).padStart(2, '0');
                    const dd = String(today.getDate()).padStart(2, '0');
                    dateParam = `?date=${yyyy}-${mm}-${dd}`;
                }

                const url = `/api/v1/finance/daily-cash-register/pdf${dateParam}`;
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(url, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                
                if (!response.ok) {
                    const errText = await response.text().catch(() => '');
                    throw new Error(`Erreur ${response.status}: Impossible de générer le journal de caisse (${errText || response.statusText}).`);
                }

                const rawBlob = await response.blob();
                if (!rawBlob || rawBlob.size === 0) {
                    throw new Error("Le document PDF généré est vide.");
                }
                
                const pdfBlob = new Blob([rawBlob], { type: 'application/pdf' });
                const blobUrl = URL.createObjectURL(pdfBlob);
                
                const link = document.createElement('a');
                link.href = blobUrl;
                link.download = `Journal_Caisse_${dateParam.replace('?date=', '')}.pdf`;
                document.body.appendChild(link);
                link.click();
                link.remove();
                
                setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
            } catch (err) {
                console.error("Erreur downloadDailyCashRegisterPdf:", err);
                alert(err.message || "Une erreur est survenue lors du téléchargement du journal de caisse.");
            }
        }
    }));
});
