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
            // Nettoyage global : fermer toutes les modales actives (fiche élève, etc.) pour éviter la superposition
            if (window.closeAllModals) {
                window.closeAllModals();
                await new Promise(resolve => setTimeout(resolve, 150)); // Attendre la fin de la transition CSS de fermeture
            }

            // Nettoyer tout ancien Blob URL avant de tenter un nouveau chargement.
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
                console.log("PDF URL:", url);

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
                console.log("PDF Blob URL assigned to iframe:", this.pdfPreviewUrl);
            } catch (err) {
                console.error("Erreur previewReceipt (Dashboard):", err);
                this.pdfLoadError = true;
            }
            // La modale s'ouvre TOUJOURS, même en cas d'erreur : les boutons restent fonctionnels.
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
                // If no date provided, use today's date formatted as YYYY-MM-DD
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
