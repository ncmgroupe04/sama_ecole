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
        // Directeur, Finance et Secrétariat tiennent une caisse (ouvrent une session, encaissent, la
        // clôturent). La règle #4 sépare l'encaissement de la santé financière AGRÉGÉE (dashboards,
        // liste globale des paiements), qui reste Directeur/Finance. Confort d'affichage : l'API garde.
        canRecordPayment: ['Directeur', 'Finance', 'Secretariat'].includes(window.auth.role),

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

        // Modèle hybride (volet 2) — modale prioritaire de recouvrement. Alimentée par
        // GET /finance/caisse/lookup dès qu'un élève sélectionné a une inscription en attente de
        // règlement (statut PendingPayment) ou un solde d'inscription non nul. L'encaissement s'y
        // fait comme ailleurs : POST /finance/payments, qui solde la dette, confirme l'inscription
        // et émet le premier reçu officiel gapless.
        recovery: {
            open: false,
            data: null,        // CaisseLookupDto
            amount: '',
            method: 'Cash',
            submitting: false,
            error: null,
            conflict: false,
            idempotencyKey: null
        },

        form: { amount: '', method: 'Cash' },
        formErrors: {},
        isSubmitting: false,

        // Résilience réseau (ticket JGK-L03) : clé générée à l'ouverture du formulaire d'encaissement
        // (voir startNewPayment/init), rejouée À L'IDENTIQUE par submitWithRetry à chaque tentative —
        // c'est elle qui garantit qu'un retry après coupure ne crée jamais un second paiement (JGK-L01).
        idempotencyKey: null,
        // 'sending' | 'retrying' | 'done' | 'failed' — piloté par onStateChange, affiché près du bouton.
        sendState: null,

        paymentResult: null, // résultat brut du POST (paymentId, receiptNumber…)
        receipt: null,       // reçu complet, chargé après coup pour l'affichage/l'impression

        // Parcours après encaissement : une fenêtre de confirmation (« Paiement validé ») s'affiche
        // d'abord ; le reçu ne s'affiche que si l'utilisateur choisit de l'imprimer/consulter.
        showConfirmDialog: false,
        showReceipt: false,

        // Marge restante sur la feuille A5, en mm (null tant que le reçu n'est pas rendu).
        // Négative = le contenu déborde ; l'impression le rognerait silencieusement.
        receiptFitMm: null,
        hasDraft: false,

        // ---------------------------------------------------------------- Session de caisse (Volume 1 §15.1)
        //
        // RecordPaymentCommandHandler refuse tout encaissement (422) hors d'une session de caisse
        // ouverte pour l'utilisateur courant — c'était déjà vrai côté serveur, mais rien ne l'exposait
        // à l'écran avant ce câblage (27/08/2026) : la recherche d'élève et le formulaire restent
        // masqués tant qu'aucune session n'est ouverte (voir x-show de la grille dans la vue).
        currentSession: null, // { sessionId, openingBalance, openedAt, totalCollected, paymentsCount }
        isLoadingSession: false,

        openSessionForm: { openingBalance: 0 },
        isOpeningSession: false,
        openSessionError: null,

        isCloseSessionModalOpen: false,
        isClosingSession: false,
        closeSessionError: null,
        // { sessionId, openingBalance, totalCollected, expectedClosingBalance, expectedCashAmount,
        //   actualCashAmount, discrepancyAmount, discrepancyReason }
        closeSessionResult: null,

        // Comptage physique obligatoire (ticket JGK-F09).
        closeSessionForm: { actualCashAmount: '', discrepancyReason: '' },
        // Révélé après un premier essai en 422 : la caisse ne tombe pas juste, un motif est requis.
        // Jamais affiché par anticipation — seul le serveur connaît les espèces attendues (fonds
        // initial + encaissements EN ESPÈCES uniquement), impossible à recalculer fiablement ici.
        closeSessionNeedsReason: false,

        async loadCurrentSession() {
            this.isLoadingSession = true;
            try {
                this.currentSession = await window.api.get('/finance/sessions/current');
            } catch {
                // 403 possible si le rôle n'a pas accès (canRecordPayment filtre déjà l'appelant réel) ;
                // dans tous les cas, l'absence de session se traite comme "pas encore ouverte".
                this.currentSession = null;
            } finally {
                this.isLoadingSession = false;
            }
        },

        async submitOpenSession() {
            this.isOpeningSession = true;
            this.openSessionError = null;
            try {
                await window.api.post('/finance/sessions/open', {
                    openingBalance: Number(this.openSessionForm.openingBalance) || 0
                });
                this.openSessionForm = { openingBalance: 0 };
                await this.loadCurrentSession();
            } catch (err) {
                this.openSessionError = window.api.toMessage(err, "Erreur lors de l'ouverture de la session de caisse.");
            } finally {
                this.isOpeningSession = false;
            }
        },

        openCloseSessionModal() {
            this.closeSessionError = null;
            this.closeSessionResult = null;
            this.closeSessionNeedsReason = false;
            this.closeSessionForm = { actualCashAmount: '', discrepancyReason: '' };
            this.isCloseSessionModalOpen = true;
        },

        closeCloseSessionModal() {
            this.isCloseSessionModalOpen = false;
        },

        /**
         * Comptage physique obligatoire (ticket JGK-F09) : Volume 1 §15.2 fige le solde théorique,
         * jamais re-clôturable ensuite. Un premier essai sans motif suffit tant que la caisse tombe
         * juste ; un écart renvoie 422 (DiscrepancyReason) et révèle le champ motif — jamais de
         * soumission automatique avec un motif deviné à la place du caissier.
         */
        async confirmCloseSession() {
            if (!this.currentSession) return;
            if (this.closeSessionForm.actualCashAmount === '' || this.closeSessionForm.actualCashAmount === null) {
                this.closeSessionError = 'Indiquez le montant réellement compté en caisse.';
                return;
            }
            this.isClosingSession = true;
            this.closeSessionError = null;
            try {
                this.closeSessionResult = await window.api.post(
                    `/finance/sessions/${this.currentSession.sessionId}/close`, {
                        actualCashAmount: Number(this.closeSessionForm.actualCashAmount) || 0,
                        discrepancyReason: this.closeSessionForm.discrepancyReason || null
                    });
                await this.loadCurrentSession(); // redevient null : la journée suivante en ouvrira une autre.
            } catch (err) {
                if (err.status === 422 && err.details && err.details.DiscrepancyReason) {
                    this.closeSessionNeedsReason = true;
                }
                this.closeSessionError = window.api.toMessage(err, 'Erreur lors de la clôture de la session.');
            } finally {
                this.isClosingSession = false;
            }
        },

        async downloadClosingReport() {
            if (!this.closeSessionResult) return;
            await this.openPdfPreview(
                `/api/v1/finance/sessions/${this.closeSessionResult.sessionId}/closing-report`,
                'Rapport de clôture de caisse',
                `Rapport-Cloture-${this.closeSessionResult.sessionId}.pdf`);
        },

        formatTime(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        },

        init() {
            this.idempotencyKey = window.networkGuard.newIdempotencyKey();
            if (this.canRecordPayment) this.loadCurrentSession();
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
                this.studentSearchError = window.api.toMessage(err, 'Erreur lors de la recherche.');
            } finally {
                this.isSearchingStudents = false;
            }
        },

        clearSelection() {
            this.selectedStudent = null;
            this.balance = null;
            this.balanceError = null;
            this.recovery.open = false;
            this.recovery.data = null;
        },

        async selectStudent(student) {
            this.selectedStudent = student;
            this.studentSearch = `${student.matricule} — ${student.fullName}`;
            await this.loadBalance();
            // Détection de dette d'inscription : ouvre la modale prioritaire de recouvrement si besoin.
            await this.checkPendingEnrollment(student.id);
        },

        // ---------------------------------------------------------------- Recouvrement d'inscription

        /**
         * Interroge GET /finance/caisse/lookup pour l'élève choisi. Si une dette d'inscription
         * existe (statut PendingPayment, ou solde non nul), ouvre la modale prioritaire pré-remplie
         * avec le reste à régler. Silencieux sur erreur : la détection est un confort, elle ne doit
         * jamais bloquer l'écran (le solde reste affiché dans le panneau principal).
         */
        async checkPendingEnrollment(studentId) {
            try {
                const lookup = await window.api.get(
                    `/finance/caisse/lookup?query=${encodeURIComponent(studentId)}`);

                if (!lookup || !lookup.hasPendingEnrollment) {
                    this.recovery.open = false;
                    this.recovery.data = null;
                    return;
                }

                this.recovery.data = lookup;
                this.recovery.amount = lookup.balanceRemaining;
                this.recovery.method = 'Cash';
                this.recovery.error = null;
                this.recovery.conflict = false;
                this.recovery.idempotencyKey = window.networkGuard.newIdempotencyKey();
                this.recovery.open = true;
            } catch {
                // 403 (rôle sans accès caisse) ou autre : on n'ouvre simplement pas la modale.
                this.recovery.open = false;
                this.recovery.data = null;
            }
        },

        closeRecovery() {
            this.recovery.open = false;
        },

        canSubmitRecovery() {
            const d = this.recovery.data;
            if (!d || this.recovery.submitting) return false;
            const amount = Number(this.recovery.amount);
            return amount > 0 && amount <= d.balanceRemaining;
        },

        /**
         * Encaisse le versement saisi dans la modale. Même contrat que submit() : POST
         * /finance/payments avec une clé d'idempotence rejouable, gestion explicite du 409 (verrou
         * xmin, règle #5 — jamais de réessai silencieux). Au succès, on ferme la modale, on recharge
         * le solde et on bascule sur la fenêtre de confirmation + reçu, exactement comme un
         * encaissement lancé depuis le panneau principal.
         */
        async submitRecovery() {
            const d = this.recovery.data;
            if (!d) return;

            this.recovery.error = null;
            this.recovery.conflict = false;
            this.recovery.submitting = true;

            try {
                this.paymentResult = await window.api.postWithRetry('/finance/payments', {
                    enrollmentId: d.enrollmentId,
                    amount: Number(this.recovery.amount),
                    method: this.recovery.method,
                    category: 'Enrollment',
                    idempotencyKey: this.recovery.idempotencyKey
                }, { onStateChange: (state) => { this.sendState = state; } });

                this.receipt = await window.api.get(
                    `/finance/payments/${this.paymentResult.paymentId}/receipt`);

                this.recovery.open = false;
                this.showReceipt = false;
                this.showConfirmDialog = true;
                if (window.formDraft) window.formDraft.clear('caisse_form');
                this.hasDraft = false;

                // Le panneau principal doit refléter le versement (solde, statut, historique).
                await this.reloadBalance();
            } catch (err) {
                if (err.status === 409) {
                    // Le solde a bougé entre l'ouverture de la modale et la validation : on relit.
                    this.recovery.conflict = true;
                } else {
                    this.recovery.error = window.api.toMessage(err, "Erreur lors de l'encaissement.");
                }
            } finally {
                this.recovery.submitting = false;
            }
        },

        /** Après un 409 dans la modale : on relit le lookup à jour avant toute nouvelle tentative. */
        async reloadRecovery() {
            this.recovery.conflict = false;
            if (this.selectedStudent) {
                await this.loadBalance();
                await this.checkPendingEnrollment(this.selectedStudent.id);
            }
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
                    : (window.api.toMessage(err, 'Erreur lors du chargement du solde.'));
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
            this.sendState = null;

            try {
                // submitWithRetry (JGK-L02) rejoue UNIQUEMENT sur coupure réseau, jamais sur une
                // réponse HTTP — la même idempotencyKey à chaque tentative fait qu'un retry rejoue le
                // paiement déjà encaissé (JGK-L01) au lieu d'en créer un second.
                this.paymentResult = await window.api.postWithRetry('/finance/payments', {
                    enrollmentId: this.balance.enrollmentId,
                    amount: Number(this.form.amount),
                    method: this.form.method,
                    idempotencyKey: this.idempotencyKey
                }, { onStateChange: (state) => { this.sendState = state; } });

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

        /** Libellé de l'état d'envoi affiché près du bouton, piloté par submitWithRetry (JGK-L03). */
        sendStateLabel() {
            switch (this.sendState) {
                case 'retrying': return 'Connexion instable — nouvelle tentative en cours, en attente d\'envoi…';
                case 'failed': return 'Échec après plusieurs tentatives — la saisie est conservée, réessayez.';
                default: return '';
            }
        },

        /** Fenêtre de confirmation → « Imprimer le reçu » : on ferme le dialogue et on révèle le reçu. */
        showReceiptFromDialog() {
            this.showConfirmDialog = false;
            this.showReceipt = true;
            // $nextTick : la feuille n'existe dans le DOM qu'après le rendu du x-show.
            this.$nextTick(() => this.checkReceiptFit());
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
            this.showReceipt = false;
            this.showConfirmDialog = false;
            this.recovery.open = false;
            this.recovery.data = null;
            this.recovery.error = null;
            this.recovery.conflict = false;
            // Nouvel encaissement = nouvelle clé (JGK-L03) : réutiliser l'ancienne rejouerait le
            // paiement précédent au lieu d'en enregistrer un nouveau.
            this.idempotencyKey = window.networkGuard.newIdempotencyKey();
            this.sendState = null;
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

        /**
         * Classe de pastille d'une échéance.
         *
         * Rendait auparavant le NOM d'une variante ('success', 'danger'…) destiné à l'attribut
         * `variant` du Tag Helper &lt;badge&gt;, via `:variant="statusBadgeVariant(...)"`. Cette
         * liaison ne pouvait pas fonctionner : `variant` est une propriété C# lue au rendu du
         * serveur, et `:variant` n'est qu'un attribut HTML de plus posé sur le &lt;span&gt; émis.
         * Résultat, toute la colonne « Statut » du tableau des échéances s'affichait en gris neutre
         * — soldé, en retard et partiel avaient exactement la même apparence.
         *
         * On rend donc directement les classes du design system, que `:class` sait appliquer.
         */
        statusBadgeClass(status) {
            switch (status) {
                case 'Paid': return 'status-badge-success';
                case 'Overdue': return 'status-badge-danger';
                case 'Partial': return 'status-badge-warning';
                default: return 'status-badge-neutral';
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

        /**
         * « Imprimer » ouvre le reçu de caisse PDF dans la modale d'aperçu partagée (visionneuse PDF
         * native du navigateur, avec ses propres boutons Imprimer / Télécharger). Plus de
         * window.print() sur le rendu HTML de la page.
         */
        async printReceipt() {
            await this.downloadPdf();
        },

        // Aperçu PDF (reçu de caisse) — état + méthodes étalés depuis le moteur partagé
        // (wwwroot/js/pdf-preview.js) ; downloadPdf/previewReceipt ci-dessous appellent openPdfPreview.
        ...window.pdfPreview.state(),

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

        // ------------------------------------------------- Reçu de caisse A5 (refonte 25/08/2026)
        //
        // Le reçu porte désormais la VENTILATION du versement — une ligne par poste imputé — au lieu
        // de l'unique « Versement reçu ». Le dû annuel, le déjà-réglé et le solde n'y figurent plus :
        // un reçu n'atteste que de la somme entrée en caisse ce jour-là (AGENTS.md règle #12).
        // Ils restent affichés dans le cadre de gauche, à l'usage du caissier.

        /**
         * Vrai quand la ventilation est imprimable. La décision vient du SERVEUR
         * (PaymentReceiptDto.HasBalancedLines) : si le détail saisi ne totalise pas exactement le
         * montant encaissé, le document se replie sur sa ligne unique — un tableau dont le détail
         * contredit le total est un faux. Le repli local reproduit la même règle, jamais une variante.
         */
        hasVentilation() {
            if (!this.receipt) return false;
            if (typeof this.receipt.hasBalancedLines === 'boolean') {
                return this.receipt.hasBalancedLines;
            }
            const lines = this.receipt.lines || [];
            if (lines.length === 0) return false;
            return lines.reduce((sum, l) => sum + l.amount, 0) === this.receipt.amount;
        },

        /**
         * Colonne « Période / Note » : le libellé propre à la ligne, à défaut la période du versement
         * entier, à défaut CHAÎNE VIDE. Jamais un tiret ni une période devinée — un reçu n'invente pas
         * la période qu'il atteste. Même cascade que PaymentReceiptDto.ResolveLineLabel.
         */
        lineLabel(line) {
            if (line && line.label && line.label.trim()) return line.label;
            const period = this.receipt ? this.receipt.referencePeriod : null;
            return period && period.trim() ? period : '';
        },

        /** Précision du bandeau de section : la période couverte par le versement, si renseignée. */
        periodHint() {
            const period = this.receipt ? this.receipt.referencePeriod : null;
            return period && period.trim() ? `Période de référence : ${period}` : '';
        },

        /** Ligne de coordonnées de l'en-tête : adresse · téléphone · e-mail, sans les trous. */
        contactLine() {
            if (!this.receipt) return '';
            return [this.receipt.schoolAddress, this.receipt.schoolPhone, this.receipt.schoolEmail]
                .filter(Boolean).join('  ·  ');
        },

        /** Ligne « NINEA … · RCCM … » : n'imprime que les mentions réellement saisies. */
        legalMentions() {
            if (!this.receipt) return '';
            return [
                this.receipt.schoolNinea ? `NINEA : ${this.receipt.schoolNinea}` : null,
                this.receipt.schoolRegistreCommerce ? `RCCM : ${this.receipt.schoolRegistreCommerce}` : null
            ].filter(Boolean).join('  ·  ');
        },

        /**
         * Écart entre la hauteur du contenu et la zone utile de la feuille (128 mm), en millimètres.
         * Positif = il reste de la place, négatif = ça déborde. Mesuré sur le rendu RÉEL : c'est la
         * seule garantie de page unique quel que soit le nombre de postes ventilés. L'impression
         * rogne le débordement — cet indicateur existe pour qu'il soit vu avant, et non subi.
         */
        checkReceiptFit() {
            const sheet = document.getElementById('receipt-printable');
            if (!sheet) { this.receiptFitMm = null; return; }

            const style = window.getComputedStyle(sheet);
            const inner = sheet.clientHeight
                - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom);

            let used = 0;
            for (const child of sheet.children) used += child.offsetHeight;

            this.receiptFitMm = (inner - used) / (96 / 25.4);
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

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return window.formatFCFA(amount);
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
