/**
 * Écran Caisse — ticket JGK-F02.
 *
 * Flux en trois temps : rechercher un élève (réutilise GET /students, comme le sélecteur de
 * réinscription de /inscriptions) → afficher le solde de son inscription sur l'année active
 * (GET /finance/students/{id}/balance) → encaisser un versement (POST /finance/payments) et en
 * imprimer le reçu officiel.
 *
 * Le solde affiché n'est qu'un APPERÇU pour guider la saisie : c'est le serveur qui vérifie et fait foi
 * (RecordPaymentCommandHandler). Deux caissiers peuvent ouvrir la même fiche en même temps — si l'un
 * encaisse pendant que l'autre saisit, le second reçoit un 409 (verrou optimiste xmin, AGENTS.md règle
 * #5) : on ne réessaie JAMAIS silencieusement, on affiche le conflit et on force un rechargement du solde.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('caisseView', () => ({
        // Seuls le Directeur et la Finance encaissent (règle #4). Confort d'affichage : l'API garde.
        canRecordPayment: window.auth.role === 'Directeur' || window.auth.role === 'Finance',

        students: [],
        studentsLoaded: false,
        isSearchingStudents: false,
        studentSearchError: null,
        studentSearch: '',
        selectedStudent: null,

        isLoadingBalance: false,
        balance: null,
        balanceError: null,
        conflictError: false,

        form: { amount: '', method: 'Cash' },
        formErrors: {},
        isSubmitting: false,

        paymentResult: null, // résultat brut du POST (paymentId, receiptNumber…)
        receipt: null,       // reçu complet, chargé après coup pour l'affichage/l'impression
        pdfError: null,

        // Parcours après encaissement : une fenêtre de confirmation (« Paiement validé ») s'affiche
        // d'abord ; le reçu ne s'affiche que si l'utilisateur choisit de l'imprimer/consulter.
        showConfirmDialog: false,
        showReceipt: false,
        hasDraft: false,

        init() {
            if (window.formDraft && window.formDraft.has('caisse_form')) {
                this.hasDraft = true;
            }
            this.$watch('form', (val) => {
                if (window.formDraft && (val.amount || this.selectedStudent)) {
                    window.formDraft.save('caisse_form', {
                        form: val,
                        student: this.selectedStudent
                    });
                }
            });
        },

        restoreDraft() {
            if (!window.formDraft) return;
            const draft = window.formDraft.load('caisse_form');
            if (!draft) return;
            if (draft.student) {
                this.selectStudent(draft.student).then(() => {
                    if (draft.form) Object.assign(this.form, draft.form);
                });
            } else if (draft.form) {
                Object.assign(this.form, draft.form);
            }
            this.hasDraft = false;
        },

        clearDraft() {
            if (window.formDraft) window.formDraft.clear('caisse_form');
            this.hasDraft = false;
        },

        // ---------------------------------------------------------------- Recherche élève

        /**
         * Recherche CÔTÉ SERVEUR (GET /students?search=…), et non un chargement d'une page fixe filtrée
         * en local : une école secondaire sénégalaise compte couramment plus de mille élèves (même
         * remarque que GetStudentsQuery), un plafond de page les rendrait invisibles à la recherche.
         * Déclenchée avec un anti-rebond (x-on:input.debounce.300ms dans la vue).
         */
        async searchStudents() {
            this.clearSelection();
            const q = this.studentSearch.trim();

            if (q.length < 2) {
                this.students = [];
                this.studentsLoaded = false;
                this.studentSearchError = null;
                return;
            }

            this.isSearchingStudents = true;
            this.studentSearchError = null;
            try {
                const page = await window.api.get(`/students?search=${encodeURIComponent(q)}&page=1&pageSize=20`);
                this.students = page.items;
                this.studentsLoaded = true;
            } catch (err) {
                this.studentSearchError = err.message || 'Erreur lors de la recherche.';
            } finally {
                this.isSearchingStudents = false;
            }
        },

        clearSelection() {
            this.selectedStudent = null;
            this.balance = null;
            this.balanceError = null;
        },

        async selectStudent(student) {
            this.selectedStudent = student;
            this.studentSearch = `${student.matricule} — ${student.fullName}`;
            await this.loadBalance();
        },

        // ---------------------------------------------------------------- Solde

        async loadBalance() {
            if (!this.selectedStudent) return;

            this.isLoadingBalance = true;
            this.balance = null;
            this.balanceError = null;
            this.conflictError = false;
            this.formErrors = {};

            try {
                this.balance = await window.api.get(`/finance/students/${this.selectedStudent.id}/balance`);
                this.form.amount = '';
            } catch (err) {
                // 404 : aucune inscription active pour cet élève — état géré par balanceError, pas une
                // erreur globale (le panneau dédié explique la marche à suivre).
                this.balanceError = err.status === 404
                    ? 'Aucune inscription active.'
                    : (err.message || 'Erreur lors du chargement du solde.');
            } finally {
                this.isLoadingBalance = false;
            }
        },

        /** Après un 409 : on ne réessaie jamais avec les anciennes données, on relit le solde à jour. */
        async reloadBalance() {
            this.conflictError = false;
            await this.loadBalance();
        },

        fillFullBalance() {
            if (this.balance) this.form.amount = this.balance.remainingBalance;
        },

        // ---------------------------------------------------------------- Encaissement

        canSubmit() {
            if (!this.balance || this.balance.remainingBalance <= 0 || this.isSubmitting) return false;
            const amount = Number(this.form.amount);
            return amount > 0 && amount <= this.balance.remainingBalance;
        },

        async submit() {
            if (!this.balance) return;

            this.formErrors = {};
            this.conflictError = false;
            this.isSubmitting = true;

            try {
                this.paymentResult = await window.api.post('/finance/payments', {
                    enrollmentId: this.balance.enrollmentId,
                    amount: Number(this.form.amount),
                    method: this.form.method
                });

                // Le résultat du POST est volontairement minimal (règle CQRS) : on relit le reçu complet
                // pour l'affichage/l'impression, comme /inscriptions le fait pour son propre reçu.
                this.receipt = await window.api.get(`/finance/payments/${this.paymentResult.paymentId}/receipt`);

                // Étape 1 : on confirme l'encaissement dans une fenêtre dédiée ; le reçu n'apparaît
                // qu'ensuite, si l'utilisateur clique « Imprimer le reçu ».
                this.showReceipt = false;
                this.showConfirmDialog = true;
                if (window.formDraft) window.formDraft.clear('caisse_form');
                this.hasDraft = false;
            } catch (err) {
                if (err.status === 409) {
                    // Solde modifié entre-temps par un autre caissier : jamais un écrasement silencieux
                    // (règle #5). On force explicitement un rechargement avant toute nouvelle tentative.
                    this.conflictError = true;
                } else {
                    this.formErrors = window.api.toFieldErrors(err, "Erreur lors de l'encaissement.");
                }
            } finally {
                this.isSubmitting = false;
            }
        },

        /** Fenêtre de confirmation → « Imprimer le reçu » : on ferme le dialogue et on révèle le reçu. */
        showReceiptFromDialog() {
            this.showConfirmDialog = false;
            this.showReceipt = true;
        },

        /** Fenêtre de confirmation → « Terminer » (ou fermeture) : repart sur une recherche vierge. */
        finishFromDialog() {
            this.showConfirmDialog = false;
            this.startNewPayment();
        },

        /** Écran du reçu → « Retour (Fermer) » : masque le reçu et revient à la recherche d'élève. */
        closeReceipt() {
            this.showReceipt = false;
            this.startNewPayment();
        },

        startNewPayment() {
            this.selectedStudent = null;
            this.balance = null;
            this.balanceError = null;
            this.conflictError = false;
            this.studentSearch = '';
            this.form = { amount: '', method: 'Cash' };
            this.formErrors = {};
            this.paymentResult = null;
            this.receipt = null;
            this.pdfError = null;
            this.showReceipt = false;
            this.showConfirmDialog = false;
        },

        formatDateOnly(dateStr) {
            if (!dateStr) return '—';
            try {
                const d = new Date(dateStr);
                if (isNaN(d.getTime())) return dateStr;
                return d.toLocaleDateString('fr-FR');
            } catch {
                return dateStr;
            }
        },

        statusBadgeVariant(status) {
            switch (status) {
                case 'Paid': return 'success';
                case 'Overdue': return 'danger';
                case 'Partial': return 'warning';
                default: return 'neutral';
            }
        },

        statusLabel(status) {
            switch (status) {
                case 'Paid': return 'Soldé';
                case 'Overdue': return 'En retard';
                case 'Partial': return 'Partiel';
                case 'Pending': return 'En attente';
                default: return status || '—';
            }
        },

        payInstallment(inst) {
            if (!inst || inst.remainingDue <= 0) return;
            this.form.amount = inst.remainingDue;
            const input = document.getElementById('caisse-amount');
            if (input) {
                input.focus();
                input.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        },

        printReceipt() {
            window.print();
        },

        showPdfModal: false,
        pdfPreviewUrl: null,
        pdfPreviewTitle: '',
        pdfDownloadName: '',
        pdfLoadError: false,

        async openPdfPreview(url, title, downloadName) {
            this.pdfError = null;
            this.pdfLoadError = false;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(url, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) throw new Error('Téléchargement du document impossible.');

                const rawBlob = await response.blob();
                const pdfBlob = new Blob([rawBlob], { type: 'application/pdf' });
                if (this.pdfPreviewUrl) {
                    URL.revokeObjectURL(this.pdfPreviewUrl);
                }
                this.pdfPreviewUrl = URL.createObjectURL(pdfBlob);
                this.pdfPreviewTitle = title || 'Document officiel';
                this.pdfDownloadName = downloadName || 'document.pdf';
                this.showPdfModal = true;
            } catch (err) {
                this.pdfError = err.message || 'Erreur lors du chargement du document.';
            }
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
            const iframe = document.getElementById('pdf-preview-frame');
            if (iframe && iframe.contentWindow) {
                iframe.contentWindow.print();
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

        /** Ouvre la modale de prévisualisation PDF pour le reçu de caisse (ticket JGK-F02 / Axe 4). */
        async downloadPdf() {
            if (!this.paymentResult) return;
            await this.openPdfPreview(
                `/api/v1/finance/payments/${this.paymentResult.paymentId}/receipt/pdf`,
                `Reçu de paiement n° ${this.paymentResult.receiptNumber}`,
                `Recu-${this.paymentResult.receiptNumber}.pdf`
            );
        },

        /** Ouvre l'aperçu et l'impression d'un reçu historique depuis le tableau des versements. */
        async previewReceipt(paymentId, receiptNumber) {
            await this.openPdfPreview(
                `/api/v1/finance/payments/${paymentId}/receipt/pdf`,
                `Reçu de paiement n° ${receiptNumber}`,
                `Recu-${receiptNumber}.pdf`
            );
        },

        // ---------------------------------------------------------------- Affichage

        methodLabel(method) {
            switch (method) {
                case 'Cash': return 'Espèces';
                case 'Cheque': return 'Chèque';
                case 'Transfer': return 'Virement';
                case 'MobileMoney': return 'Mobile Money (Wave / Orange Money)';
                default: return method;
            }
        },

        /** FCFA : entiers, séparateur de milliers français. Pas de décimales — la monnaie n'en a pas. */
        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount) + ' FCFA';
        },

        /** Bas de reçu « Fait à [ville], le [date] » (référence de design §1.6) ; sans ville, on abrège. */
        faitMention() {
            if (!this.receipt) return '';
            const date = this.formatDate(this.receipt.paidAt);
            return this.receipt.schoolCity ? `Fait à ${this.receipt.schoolCity}, le ${date}` : `Fait le ${date}`;
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            const day = String(d.getDate()).padStart(2, '0');
            const month = String(d.getMonth() + 1).padStart(2, '0');
            return `${day}/${month}/${d.getFullYear()}`;
        }
    }));
});
