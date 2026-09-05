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

        form: { amount: '', method: 'Cash' },
        formErrors: {},
        isSubmitting: false,

        // Guichet rapide (pop-up « Encaissement des frais dus ») : s'ouvre dès qu'un élève ayant un
        // montant échu est sélectionné. `checked` est aligné index par index sur quickPayRows() ; la
        // sélection est CONTIGUË — décocher une ligne décoche aussi toutes les suivantes, car
        // l'imputation en base suit l'ordre des échéances (InstallmentScheduleCalculator), donc seul
        // un préfixe de la liste peut passer proprement à « Réglé ».
        quickPay: { open: false, method: 'Cash', amount: '', checked: [] },

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
                // silence-volontaire: 403 possible si le rôle n'a pas accès (canRecordPayment filtre
                // déjà l'appelant réel) ; dans tous les cas, l'absence de session se traite comme
                // "pas encore ouverte" — l'écran affiche alors son bandeau d'ouverture, qui EST la
                // bonne conduite à tenir. Un message d'erreur n'ajouterait rien d'actionnable.
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
                this.selectStudent(draft.student, { autoQuickPay: false }).then(() => {
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
                // Route dédiée (finance/students/search), pas la recherche générique /students : chaque
                // ligne porte déjà le solde de l'inscription active, pour signaler dans LA LISTE — avant
                // toute sélection — qu'un élève vient d'être inscrit par le secrétariat mais n'a encore
                // rien versé (voir badgeFor ci-dessous).
                this.students = await window.api.get(`/finance/students/search?q=${encodeURIComponent(q)}`);
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
            this.quickPay.open = false;
        },

        async selectStudent(student, { autoQuickPay = true } = {}) {
            this.selectedStudent = student;
            this.studentSearch = `${student.matricule} — ${student.fullName}`;
            await this.loadBalance();
            // Guichet rapide : dès qu'un élève est choisi et qu'il a un montant échu, on ouvre la
            // pop-up d'encaissement (frais dus pré-cochés). Les cas particuliers — montant libre,
            // solde intégral, consultation de l'historique — restent accessibles derrière [Annuler],
            // sur le panneau détaillé qui reste affiché. Jamais à la restauration d'un brouillon
            // (autoQuickPay=false) : l'utilisateur y reprend une saisie manuelle en cours.
            if (autoQuickPay && this.balance && this.dueNowTotal > 0 && !this.showReceipt) {
                this.openQuickPay();
            }
        },

        // ---------------------------------------------------------------- Guichet rapide (pop-up)

        /** Échéances de l'ENGAGEMENT INITIAL encore dues — les lignes proposées dans la pop-up. */
        quickPayRows() {
            return (this.balance?.installments ?? []).filter((i) => i.isInitialScope && i.remainingDue > 0);
        },

        /** Total des lignes actuellement COCHÉES — pilote le « Total à encaisser » et le bouton. */
        quickPayTotal() {
            return this.quickPayRows().reduce(
                (sum, row, idx) => sum + (this.quickPay.checked[idx] ? row.remainingDue : 0), 0);
        },

        openQuickPay() {
            const rows = this.quickPayRows();
            if (rows.length === 0) return;
            this.quickPay.checked = rows.map(() => true); // tout coché par défaut
            this.quickPay.method = 'Cash';
            this.quickPay.amount = this.quickPayTotal();
            this.formErrors = {};
            this.conflictError = false;
            this.quickPay.open = true;
        },

        /** [Annuler] : referme la pop-up et laisse le panneau détaillé accessible (cas particuliers). */
        closeQuickPay() {
            this.quickPay.open = false;
        },

        /**
         * Sélection CONTIGUË : cocher la ligne i coche aussi 0..i-1 ; décocher la ligne i décoche
         * aussi i+1..fin. On ne peut donc régler qu'un PRÉFIXE des frais dus — c'est exactement ce
         * que l'imputation oldest-first de InstallmentScheduleCalculator sait refléter en « Réglé ».
         */
        onQuickRowToggle(i) {
            if (this.quickPay.checked[i]) {
                for (let j = 0; j < i; j++) this.quickPay.checked[j] = true;
            } else {
                for (let j = i + 1; j < this.quickPay.checked.length; j++) this.quickPay.checked[j] = false;
            }
            this.quickPay.amount = this.quickPayTotal();
        },

        /** « Mensualité (Mois 1) » → « Mois 1 » pour la colonne « Période / Note » du reçu ventilé. */
        quickPayNote(designation) {
            const match = /\(([^)]+)\)\s*$/.exec(designation || '');
            return match ? match[1] : null;
        },

        async submitQuickPay() {
            if (!this.balance) return;
            const rows = this.quickPayRows();
            const chosen = rows.filter((_, idx) => this.quickPay.checked[idx]);
            if (chosen.length === 0) return;

            const checkedTotal = chosen.reduce((sum, r) => sum + r.remainingDue, 0);
            const amount = Number(this.quickPay.amount) || checkedTotal;

            // Ventilation (une ligne de reçu par poste) seulement si le montant perçu correspond
            // EXACTEMENT au total coché ET que chaque poste porte sa catégorie de frais (échéancier
            // dérivé, pas un plan personnalisé). Sinon le reçu retombe sur sa ligne unique — garde-fou
            // comptable, cf. PaymentReceiptDto.HasBalancedLines.
            let breakdowns = null;
            if (amount === checkedTotal && chosen.every((r) => r.feeCategoryId)) {
                breakdowns = chosen.map((r) => ({
                    feeCategoryId: r.feeCategoryId,
                    amountAllocated: r.remainingDue,
                    label: this.quickPayNote(r.designation)
                }));
            }

            const ok = await this.recordPayment({ amount, method: this.quickPay.method, breakdowns });
            if (ok) this.quickPay.open = false;
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

        /**
         * MONTANT ÉCHU à ce jour : l'engagement initial (frais d'inscription, uniforme… + le PREMIER
         * mois de scolarité) non encore réglé — jamais les mensualités FUTURES, qui n'ont pas lieu
         * d'être encaissées d'avance. C'est ce que le secrétariat programme à l'inscription et que le
         * tuteur règle EN UNE FOIS en arrivant à la caisse, muni de sa fiche.
         *
         * Le serveur le calcule (StudentBalanceDto.dueNowTotal) à partir des échéances marquées
         * `isInitialScope` — source de vérité unique. Le repli ne sert qu'à un payload servi par un
         * cache antérieur : il recompose la même somme depuis les mêmes échéances.
         *
         * PLUS de filtre « date d'échéance <= aujourd'hui » : une année qui démarre le mois prochain
         * n'a AUCUNE échéance passée, et ce montant valait alors 0 à tort — le parent doit pourtant
         * déjà régler l'inscription et le 1er mois.
         */
        get dueNowTotal() {
            if (!this.balance) return 0;
            if (typeof this.balance.dueNowTotal === 'number') return this.balance.dueNowTotal;
            return (this.balance.installments || [])
                .filter((inst) => inst.isInitialScope && inst.remainingDue > 0)
                .reduce((sum, inst) => sum + inst.remainingDue, 0);
        },

        fillDueNow() {
            const total = this.dueNowTotal;
            if (total > 0) this.form.amount = total;
        },

        // ---------------------------------------------------------------- Encaissement

        canSubmit() {
            if (!this.balance || this.balance.remainingBalance <= 0 || this.isSubmitting) return false;
            const amount = Number(this.form.amount);
            return amount > 0 && amount <= this.balance.remainingBalance;
        },

        async submit() {
            if (!this.balance) return;
            await this.recordPayment({
                amount: Number(this.form.amount),
                method: this.form.method
            });
        },

        /**
         * Cœur de l'encaissement, partagé par le formulaire détaillé (submit) et le guichet rapide
         * (submitQuickPay). Renvoie true si le versement est passé (fenêtre de confirmation ouverte),
         * false si l'erreur a été traitée à l'écran (409 concurrentiel, ou 400 de validation).
         *
         * @param {{amount:number, method:string, breakdowns?:Array}} p — `breakdowns` : ventilation
         *   facultative (une entrée {feeCategoryId, amountAllocated, label} par poste imputé). Omise,
         *   le reçu porte sa ligne unique « Versement reçu ».
         */
        async recordPayment({ amount, method, breakdowns }) {
            this.formErrors = {};
            this.conflictError = false;
            this.isSubmitting = true;
            this.sendState = null;

            try {
                const payload = {
                    enrollmentId: this.balance.enrollmentId,
                    amount,
                    method,
                    idempotencyKey: this.idempotencyKey
                };
                if (breakdowns && breakdowns.length) payload.breakdowns = breakdowns;

                // submitWithRetry (JGK-L02) rejoue UNIQUEMENT sur coupure réseau, jamais sur une
                // réponse HTTP — la même idempotencyKey à chaque tentative fait qu'un retry rejoue le
                // paiement déjà encaissé (JGK-L01) au lieu d'en créer un second.
                this.paymentResult = await window.api.postWithRetry('/finance/payments', payload,
                    { onStateChange: (state) => { this.sendState = state; } });

                // Le résultat du POST est volontairement minimal (règle CQRS) : on relit le reçu complet
                // pour l'affichage/l'impression, comme /inscriptions le fait pour son propre reçu.
                this.receipt = await window.api.get(`/finance/payments/${this.paymentResult.paymentId}/receipt`);

                // Étape 1 : on confirme l'encaissement dans une fenêtre dédiée ; le reçu n'apparaît
                // qu'ensuite, si l'utilisateur clique « Imprimer le reçu ».
                this.showReceipt = false;
                this.showConfirmDialog = true;
                if (window.formDraft) window.formDraft.clear('caisse_form');
                this.hasDraft = false;
                return true;
            } catch (err) {
                if (err.status === 409) {
                    // Solde modifié entre-temps par un autre caissier : jamais un écrasement silencieux
                    // (règle #5). On force explicitement un rechargement avant toute nouvelle tentative.
                    this.conflictError = true;
                } else {
                    this.formErrors = window.api.toFieldErrors(err, "Erreur lors de l'encaissement.");
                }
                return false;
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
            this.quickPay = { open: false, method: 'Cash', amount: '', checked: [] };
            this.formErrors = {};
            this.paymentResult = null;
            this.receipt = null;
            this.showReceipt = false;
            this.showConfirmDialog = false;
            // La feuille A5 est démontée avec `receipt` (x-if) : sa mesure de tenue en page ne
            // vaut plus rien. La garder afficherait le verdict du reçu PRÉCÉDENT sur le suivant.
            this.receiptFitMm = null;
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

            // Le filet bas ne s'applique qu'À L'IMPRESSION : la feuille mesurée ici est celle de
            // L'ÉCRAN, qui ne le porte pas encore. Sans cette soustraction, l'indicateur annonce
            // 4 mm de place qui n'existent pas au moment d'imprimer — mesuré le 03/09/2026 :
            // 127,4 mm annoncés contre 124,1 mm réellement disponibles. Un reçu affiché comme
            // « tient sur une page » avec 2 mm libres serait alors rogné en silence, ce que cet
            // indicateur existe précisément pour empêcher. Valeur lue sur l'élément : le CSS
            // (--receipt-print-bottom-inset) reste la seule source de vérité.
            const printInsetMm = parseFloat(style.getPropertyValue('--receipt-print-bottom-inset')) || 0;
            this.receiptFitMm = (inner - used) / (96 / 25.4) - printInsetMm;
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
