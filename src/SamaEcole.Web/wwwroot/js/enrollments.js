/**
 * Écran Inscriptions — ticket JGK-E01.
 *
 * Point de convergence du MVP : on choisit une classe, l'écran affiche EN DIRECT les frais dus
 * (frais ponctuels comptés une fois, mensualités multipliées par le nombre de mensualités de l'année
 * — réglage TuitionMonthsPerYear), puis l'enregistrement crée l'élève (avec son matricule) et son
 * inscription en un seul appel, et renvoie le reçu.
 *
 * Le panneau « Frais » est une AIDE AU CALCUL, rien de plus : il n'encaisse rien (le secrétariat
 * n'enregistre aucun versement, AGENTS.md règle #4). Le simulateur `simMonths` / `simSubtotal()` sert
 * uniquement à annoncer un montant au parent — tout règlement, y compris le premier, se fait à la
 * Caisse (/caisse, RecordPaymentCommand).
 *
 * Le calcul affiché n'est qu'un APERÇU : c'est le serveur qui recalcule et fait foi (voir
 * CreateEnrollmentCommandHandler). Le rôle est relu du JWT pour masquer le formulaire aux rôles qui
 * n'inscrivent pas, mais l'API répond 403 de toute façon (le service Finance ne compose jamais un
 * montant dû, AGENTS.md règle #4).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('enrollmentsView', () => ({
        // Seuls le Directeur et le Secrétariat inscrivent (règle #4). Confort d'affichage : l'API garde.
        canEnroll: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',

        isLoading: false,
        error: null,

        classrooms: [],
        recurringByCategory: {}, // { feeCategoryId: isRecurring }
        feesByClassroom: {},     // { classroomId: [{ feeCategoryId, designation, isRecurring, unitAmount }] }
        tuitionMonths: 9,
        activeYear: null,

        // Module Internat (Task 17) — confort d'affichage, comme partout ailleurs dans le produit :
        // la vraie garde est CreateEnrollmentCommandHandler (422 si le module n'est pas activé). Lu sur
        // la même réponse /schools/current/settings que tuitionMonths (loadReferenceData) — pas d'appel
        // dédié, pour ne pas dupliquer la requête déjà faite sur cet écran.
        internatEnabled: false,
        // Chambres avec au moins une place libre, pour le <select> du régime Interne/Demi-pensionnaire.
        // Rechargé à chaque changement de régime (l'occupation évolue au fil des inscriptions).
        availableRooms: [],

        // Élèves pour la réinscription (chargés à la demande)
        students: [],
        studentsLoaded: false,
        studentSearch: '',

        // Formulaire
        mode: 'NewEnrollment',
        form: {
            classroomId: '',
            isRepeating: false, // Classe redoublée (feature F) — porté par l'inscription, coché sur le bulletin.
            fullName: '',
            birthDate: '',
            birthPlace: '', // Obligatoire pour une nouvelle inscription (feature E).
            gender: 'M',
            guardianName: '',
            guardianPhone: '',
            studentId: '',

            // --- Régime & Hébergement (module Internat, Task 17) ---
            boardingStatus: 'Externe',
            roomId: null,
            // Vrai par défaut : quand l'école a une catégorie de frais IsBoardingFee sur la classe
            // choisie, on l'ajoute par défaut à la fiche financière — décocher est l'exception,
            // pas la règle (voir BoardingFeeLineBuilder, sans effet si aucune catégorie ne correspond).
            includeBoardingFee: true
        },
        formErrors: {},
        isSubmitting: false,

        /**
         * Simulateur d'aide au calcul — PUREMENT indicatif, jamais transmis au serveur. Nombre de
         * mensualités que le secrétaire veut chiffrer pour le parent ; simSubtotal() en déduit un
         * sous-total. Aucun encaissement : le règlement se fait à la Caisse.
         */
        simMonths: 1,

        // Reçu émis
        receipt: null,

        // Parcours après enregistrement : une fois l'inscription validée, on affiche d'abord une
        // fenêtre de confirmation (« Inscription validée »), et le reçu ne s'affiche que si
        // l'utilisateur choisit de l'imprimer/consulter — il n'est plus jeté directement à l'écran.
        showConfirmDialog: false,
        showReceipt: false,
        hasDraft: false,

        // Marge restante sur la feuille A5, en mm (null tant que le reçu n'est pas rendu).
        // Négative = le contenu déborde ; l'impression le rognerait silencieusement.
        receiptFitMm: null,

        async init() {
            await this.loadReferenceData();
            await this.applyStudentFromQuery();
            if (window.formDraft && window.formDraft.has('enrollment_form')) {
                this.hasDraft = true;
            }
            this.$watch('form', (val) => {
                if (window.formDraft && (val.fullName || val.studentId || val.classroomId)) {
                    window.formDraft.save('enrollment_form', {
                        mode: this.mode,
                        form: val,
                        simMonths: this.simMonths
                    });
                }
            });
        },

        restoreDraft() {
            if (!window.formDraft) return;
            const draft = window.formDraft.load('enrollment_form');
            if (!draft) return;
            if (draft.mode) this.selectMode(draft.mode);
            if (draft.form) Object.assign(this.form, draft.form);
            if (draft.simMonths) this.simMonths = draft.simMonths;
            this.hasDraft = false;
        },

        clearDraft() {
            if (window.formDraft) window.formDraft.clear('enrollment_form');
            this.hasDraft = false;
        },

        /**
         * Raccourci « Inscrire maintenant » venu de l'écran Élèves (?studentId=…&matricule=…) :
         * on bascule en réinscription et on pré-sélectionne l'élève. Le matricule sert de
         * recherche ciblée quand l'élève n'est pas dans le lot initial (plafonné à 100). Sans
         * correspondance, on laisse le formulaire en réinscription pour une sélection manuelle.
         */
        async applyStudentFromQuery() {
            const params = new URLSearchParams(window.location.search);
            const studentId = params.get('studentId');
            if (!studentId) return;

            await this.selectMode('ReEnrollment');

            let student = this.students.find((s) => s.id === studentId);
            if (!student) {
                const term = (params.get('matricule') || '').trim();
                if (term) {
                    try {
                        const page = await window.api.get(
                            `/students?page=1&pageSize=20&search=${encodeURIComponent(term)}`);
                        student = (page.items || []).find((s) => s.id === studentId);
                        if (student) {
                            this.students = [student, ...this.students.filter((s) => s.id !== student.id)];
                        }
                    } catch (err) {
                        /* recherche ciblée impossible : l'utilisateur sélectionnera manuellement */
                    }
                }
            }

            if (student) this.selectStudent(student);
        },

        async loadReferenceData() {
            this.isLoading = true;
            this.error = null;
            try {
                const [classrooms, categories, fees, years, settings] = await Promise.all([
                    window.api.get('/classrooms'),
                    window.api.get('/finance/fee-categories'),
                    window.api.get('/finance/fees'),
                    window.api.get('/school-years'),
                    window.api.get('/schools/current/settings')
                ]);

                this.classrooms = classrooms;
                this.tuitionMonths = settings.tuitionMonthsPerYear;
                this.activeYear = years.find((y) => y.isActive) || null;

                this.internatEnabled = !!settings && settings.isInternatEnabled === true;
                if (this.internatEnabled) await this.loadAvailableRooms();

                this.recurringByCategory = {};
                categories.forEach((c) => { this.recurringByCategory[c.id] = c.isRecurring; });

                this.feesByClassroom = {};
                fees.forEach((f) => {
                    (this.feesByClassroom[f.classroomId] ||= []).push({
                        feeCategoryId: f.feeCategoryId,
                        designation: f.feeCategoryName,
                        isRecurring: !!this.recurringByCategory[f.feeCategoryId],
                        unitAmount: f.amount
                    });
                });
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des données d\'inscription.');
            } finally {
                this.isLoading = false;
            }
        },

        /**
         * Chambres avec au moins une place libre — réutilise le tableau de bord Internat (Task 15)
         * plutôt qu'une route dédiée : l'écran /internat en a déjà besoin, une seconde requête aussi
         * légère n'apporterait rien. Échec silencieux (module désactivé entretemps, RequireModule
         * ayant déjà tranché le sort de l'écran, ou 403 transitoire) : le sélecteur reste vide plutôt
         * que de bloquer l'inscription — le champ garde son astérisque required, l'utilisateur verra
         * l'absence d'option.
         */
        async loadAvailableRooms() {
            try {
                const dashboard = await window.api.get('/internat/dashboard');
                this.availableRooms = (dashboard.rooms || []).filter((r) => r.occupantsCount < r.capacity);
            } catch {
                // silence-volontaire: module désactivé entretemps ou aucune chambre — le sélecteur
                // reste simplement vide (champ toujours `required`), pas d'incident à signaler.
                this.availableRooms = [];
            }
        },

        // ---------------------------------------------------------------- Aperçu des frais

        /**
         * Recompose les lignes de frais de la classe sélectionnée, EXACTEMENT comme le serveur :
         * une mensualité est multipliée par le nombre de mensualités de l'année, un frais ponctuel
         * jamais. Trié ponctuels d'abord, comme le reçu.
         */
        previewLines() {
            const fees = this.feesByClassroom[this.form.classroomId] || [];
            return fees
                .map((f) => {
                    const months = f.isRecurring ? this.tuitionMonths : 1;
                    return {
                        feeCategoryId: f.feeCategoryId,
                        designation: f.designation,
                        isRecurring: f.isRecurring,
                        unitAmount: f.unitAmount,
                        months,
                        lineTotal: f.unitAmount * months
                    };
                })
                .sort((a, b) =>
                    (a.isRecurring - b.isRecurring) || a.designation.localeCompare(b.designation));
        },

        previewTotal() {
            return this.previewLines().reduce((sum, l) => sum + l.lineTotal, 0);
        },

        hasFeesForSelectedClass() {
            return (this.feesByClassroom[this.form.classroomId] || []).length > 0;
        },

        // ---------------------------------------------------------------- Aide au calcul (simulateur)

        /** Remet le simulateur à 1 mois — appelé au changement de classe (le barème change). */
        resetSimulator() {
            this.simMonths = 1;
        },

        /**
         * Sous-total INDICATIF : tous les frais ponctuels (réglés une fois) + les mensualités prises
         * `simMonths` fois. Purement local, jamais transmis — sert à annoncer un montant au parent
         * avant qu'il passe à la Caisse.
         */
        simSubtotal() {
            const months = Math.max(Number(this.simMonths) || 1, 1);
            return this.previewLines().reduce(
                (sum, line) => sum + (line.isRecurring ? line.unitAmount * months : line.lineTotal), 0);
        },

        // ---------------------------------------------------------------- Réinscription

        async selectMode(mode) {
            this.mode = mode;
            this.formErrors = {};
            if (mode === 'ReEnrollment' && !this.studentsLoaded) {
                await this.loadStudents();
            }
        },

        async loadStudents() {
            try {
                // 100 = GetStudentsQueryValidator.MaxPageSize, le plafond serveur anti-DoS.
                const page = await window.api.get('/students?page=1&pageSize=100');
                this.students = page.items;
                this.studentsLoaded = true;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des élèves.');
            }
        },

        filteredStudents() {
            const q = this.studentSearch.trim().toLowerCase();
            if (!q) return this.students;
            return this.students.filter((s) =>
                s.fullName.toLowerCase().includes(q) || s.matricule.toLowerCase().includes(q));
        },

        selectStudent(student) {
            this.form.studentId = student.id;
            this.studentSearch = `${student.matricule} — ${student.fullName}`;
        },

        // ---------------------------------------------------------------- Enregistrement

        canSubmit() {
            if (!this.activeYear || !this.form.classroomId || this.isSubmitting) return false;
            return this.mode === 'ReEnrollment' ? !!this.form.studentId : !!this.form.fullName;
        },

        /**
         * Champs Régime & Hébergement communs aux deux branches du payload — un élève Externe ne
         * transmet ni chambre ni pension, même si le formulaire en gardait une valeur résiduelle
         * (ex. régime rebasculé sur Externe après avoir choisi une chambre) : CreateEnrollmentCommandValidator
         * rejette un RoomId sur un régime Externe (règle miroir, voir sa validation).
         */
        boardingPayload() {
            const isBoarder = this.form.boardingStatus !== 'Externe';
            return {
                boardingStatus: this.form.boardingStatus,
                roomId: isBoarder ? this.form.roomId : null,
                includeBoardingFee: isBoarder ? this.form.includeBoardingFee : false
            };
        },

        async submit() {
            this.formErrors = {};
            this.isSubmitting = true;

            // L'inscription fige la dette et rien d'autre : aucun encaissement n'est transmis. Le
            // règlement se fait ensuite à la Caisse (/caisse).
            const command = this.mode === 'ReEnrollment'
                ? {
                    type: 'ReEnrollment',
                    classroomId: this.form.classroomId,
                    isRepeating: this.form.isRepeating,
                    studentId: this.form.studentId,
                    ...this.boardingPayload()
                }
                : {
                    type: 'NewEnrollment',
                    classroomId: this.form.classroomId,
                    isRepeating: this.form.isRepeating,
                    fullName: this.form.fullName,
                    birthDate: this.form.birthDate || null,
                    birthPlace: this.form.birthPlace || null,
                    gender: this.form.gender,
                    guardianName: this.form.guardianName || null,
                    guardianPhone: this.form.guardianPhone || null,
                    ...this.boardingPayload()
                };

            try {
                this.receipt = await window.api.post('/enrollments', command);
                // Étape 1 : on confirme l'enregistrement dans une fenêtre dédiée. Le reçu n'apparaît
                // qu'ensuite, si l'utilisateur clique « Imprimer le reçu ».
                this.showReceipt = false;
                this.showConfirmDialog = true;
                if (window.formDraft) window.formDraft.clear('enrollment_form');
                this.hasDraft = false;
            } catch (err) {
                this.formErrors = window.api.toFieldErrors(err, "Erreur lors de l'inscription.");
            } finally {
                this.isSubmitting = false;
            }
        },

        /** Fenêtre de confirmation → « Imprimer le reçu » : on ferme le dialogue et on révèle le reçu. */
        showReceiptFromDialog() {
            this.showConfirmDialog = false;
            this.showReceipt = true;
            // $nextTick : la feuille n'existe dans le DOM qu'après le rendu du x-show.
            this.$nextTick(() => this.checkReceiptFit());
        },

        /**
         * Fenêtre de confirmation → « Terminer » (ou fermeture) : on n'affiche pas le reçu et on repart
         * sur un formulaire vierge, prêt pour la prochaine inscription.
         */
        finishFromDialog() {
            this.showConfirmDialog = false;
            this.startNewEnrollment();
        },

        /**
         * Écran du reçu → « Retour (Fermer) » : on masque le reçu et on revient au formulaire pour
         * enchaîner une inscription du même type (mode conservé) que celle qui vient d'être validée.
         */
        closeReceipt() {
            this.showReceipt = false;
            this.receipt = null;
            const mode = this.mode;
            this.startNewEnrollment();
            this.mode = mode;
        },

        startNewEnrollment() {
            this.receipt = null;
            this.showReceipt = false;
            this.showConfirmDialog = false;
            // La feuille A5 est démontée avec `receipt` (x-if) : sa mesure de tenue en page ne
            // vaut plus rien. La garder afficherait le verdict du reçu PRÉCÉDENT sur le suivant.
            this.receiptFitMm = null;
            this.closePdfPreview();
            this.form = {
                classroomId: '',
                isRepeating: false,
                fullName: '',
                birthDate: '',
                birthPlace: '',
                gender: 'M',
                guardianName: '',
                guardianPhone: '',
                studentId: '',
                boardingStatus: 'Externe',
                roomId: null,
                includeBoardingFee: true
            };
            this.studentSearch = '';
            this.formErrors = {};
            this.mode = 'NewEnrollment';
            this.simMonths = 1;
        },

        /**
         * « Imprimer » ouvre l'attestation PDF dans la modale d'aperçu partagée (visionneuse PDF
         * native du navigateur, avec ses propres boutons Imprimer / Télécharger). Plus de
         * window.print() sur le rendu HTML de la page : une seule pièce, la même partout.
         */
        async printReceipt() {
            await this.downloadPdf();
        },

        // Aperçu PDF (reçu, certificat) — état + méthodes étalés depuis le moteur partagé
        // (wwwroot/js/pdf-preview.js) ; downloadPdf/downloadCertificatePdf ci-dessous appellent openPdfPreview.
        ...window.pdfPreview.state(),

        /**
         * Télécharge/Prévisualise le reçu officiel en PDF via la modale (Ticket JGK-E02 / Axe 4).
         */
        async downloadPdf() {
            if (!this.receipt) return;
            await this.openPdfPreview(
                `/api/v1/enrollments/${this.receipt.enrollmentId}/receipt/pdf`,
                `Attestation d'inscription n° ${this.receiptReference()}`,
                `Attestation-Inscription-${this.receipt.receiptNumber}.pdf`
            );
        },

        /**
         * Télécharge/Prévisualise le Certificat de Scolarité en PDF via la modale (Ticket JGK-E03 / Axe 2).
         */
        async downloadCertificatePdf() {
            if (!this.receipt) return;
            await this.openPdfPreview(
                `/api/v1/enrollments/${this.receipt.enrollmentId}/certificate/pdf`,
                `Certificat de Scolarité`,
                `Certificat-Scolarite-${this.receipt.matricule}.pdf`
            );
        },

        // ---------------------------------------------------------------- Affichage

        classroomLabel(classroom) {
            return `${classroom.name} — ${classroom.level}`;
        },

        typeLabel(type) {
            return type === 'ReEnrollment' ? 'Réinscription' : 'Nouvelle inscription';
        },

        // ------------------------------------------------- Attestation A5 (refonte 25/08/2026)
        //
        // L'attestation n'affiche PLUS le cumul annuel : ni totalDue, ni « × N mois », ni reste à
        // payer. Elle porte l'engagement initial à régler auprès de la comptabilité, puis les
        // tarifs mensuels UNITAIRES. Voir docs/design-references/README.md §1.
        //
        // Les trois valeurs viennent du serveur (EnrollmentReceiptDto les calcule), pour que
        // l'écran, l'impression navigateur et le PDF appliquent la MÊME règle métier. Le repli
        // local n'existe que pour un payload servi par un cache antérieur à la refonte — il
        // reproduit le calcul du serveur à l'identique, jamais une variante.

        /** Engagement initial : le frais ponctuel entier, ou UNE seule mensualité par ligne récurrente. */
        settlementTotal() {
            if (!this.receipt) return 0;
            if (typeof this.receipt.initialSettlementTotal === 'number') {
                return this.receipt.initialSettlementTotal;
            }
            return (this.receipt.lines || []).reduce((sum, line) => sum + line.unitAmount, 0);
        },

        /** Échéancier : les seules lignes RÉCURRENTES. Un frais ponctuel n'a pas d'échéance. */
        monthlyLines() {
            if (!this.receipt) return [];
            if (Array.isArray(this.receipt.monthlyLines)) {
                return this.receipt.monthlyLines;
            }
            return (this.receipt.lines || []).filter(line => line.isRecurring);
        },

        /** Total mensuel : somme des mensualités UNITAIRES, jamais multipliée par le nombre de mois. */
        monthlyTotal() {
            if (!this.receipt) return 0;
            if (typeof this.receipt.monthlyTotal === 'number') {
                return this.receipt.monthlyTotal;
            }
            return this.monthlyLines().reduce((sum, line) => sum + line.unitAmount, 0);
        },

        /**
         * Écart entre la hauteur du contenu et la zone utile de la feuille (128 mm), en millimètres.
         * Positif = il reste de la place, négatif = ça déborde.
         *
         * Mesuré sur le rendu RÉEL, pas estimé : c'est la seule façon de garantir la page unique
         * quelles que soient la longueur des libellés de frais et la police effectivement chargée.
         * L'impression rogne le débordement (overflow:hidden) — cet indicateur existe pour que le
         * secrétariat le VOIE avant d'imprimer, plutôt que de découvrir une pièce tronquée.
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

        /** Ligne « NINEA … · RCCM … » de l'en-tête : n'imprime que les mentions réellement saisies. */
        legalMentions() {
            if (!this.receipt) return '';
            return [
                this.receipt.schoolNinea ? `NINEA : ${this.receipt.schoolNinea}` : null,
                this.receipt.schoolRegistreCommerce ? `RCCM : ${this.receipt.schoolRegistreCommerce}` : null
            ].filter(Boolean).join('  ·  ');
        },

        /** Ligne de coordonnées de l'en-tête : adresse · téléphone · e-mail, sans les trous. */
        contactLine() {
            if (!this.receipt) return '';
            return [this.receipt.schoolAddress, this.receipt.schoolPhone, this.receipt.schoolEmail]
                .filter(Boolean).join('  ·  ');
        },

        /** Délègue à window.formatFCFA (wwwroot/js/formatters.js, chargé par _Layout) : source
         *  unique du format monétaire, alignée sur le FormatMoney des PDF. Ne pas réécrire ici. */
        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return window.formatFCFA(amount);
        },

        /** Numéro officiel du reçu (ticket JGK-E02), ex. « REC-2025-0002 ». */
        receiptReference() {
            return this.receipt ? this.receipt.receiptNumber : '';
        },

        /** Bas de reçu « Fait à [ville], le [date] » (référence de design §1.6) ; sans ville, on abrège. */
        faitMention() {
            if (!this.receipt) return '';
            const date = this.formatDate(this.receipt.enrolledAt);
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
