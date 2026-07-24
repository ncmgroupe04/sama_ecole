/**
 * Écran Inscriptions — ticket JGK-E01.
 *
 * Point de convergence du MVP : on choisit une classe, l'écran affiche EN DIRECT les frais dus
 * (frais ponctuels comptés une fois, mensualités multipliées par le nombre de mensualités de l'année
 * — réglage TuitionMonthsPerYear), puis l'enregistrement crée l'élève (avec son matricule) et son
 * inscription en un seul appel, et renvoie le reçu.
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
            studentId: ''
        },
        formErrors: {},
        isSubmitting: false,

        /**
         * Encaissement du jour, ventilé : { [feeCategoryId]: { checked, months } }. Le guichet coche ce
         * que le tuteur règle réellement (inscription, tenue, 1re mensualité…) ; seul ce qui est coché
         * figure sur le reçu. Les MONTANTS ne sont pas transmis — le serveur les reprend du barème
         * (règle #4) ; ce qui part dans la requête n'est que « cette catégorie, sur N mois ».
         */
        collected: {},
        paymentMethod: 'Cash',

        // Reçu émis
        receipt: null,
        pdfError: null,

        // Parcours après enregistrement : une fois l'inscription validée, on affiche d'abord une
        // fenêtre de confirmation (« Inscription validée »), et le reçu ne s'affiche que si
        // l'utilisateur choisit de l'imprimer/consulter — il n'est plus jeté directement à l'écran.
        showConfirmDialog: false,
        showReceipt: false,
        hasDraft: false,

        async init() {
            await this.loadReferenceData();
            if (window.formDraft && window.formDraft.has('enrollment_form')) {
                this.hasDraft = true;
            }
            this.$watch('form', (val) => {
                if (window.formDraft && (val.fullName || val.studentId || val.classroomId)) {
                    window.formDraft.save('enrollment_form', {
                        mode: this.mode,
                        form: val,
                        collected: this.collected,
                        paymentMethod: this.paymentMethod
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
            if (draft.collected) Object.assign(this.collected, draft.collected);
            if (draft.paymentMethod) this.paymentMethod = draft.paymentMethod;
            this.hasDraft = false;
        },

        clearDraft() {
            if (window.formDraft) window.formDraft.clear('enrollment_form');
            this.hasDraft = false;
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
                this.error = err.message || 'Erreur lors du chargement des données d\'inscription.';
            } finally {
                this.isLoading = false;
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

        // ---------------------------------------------------------------- Encaissement du jour

        /**
         * Réinitialise la sélection au changement de classe : les frais ne sont pas les mêmes d'une
         * classe à l'autre, garder les cases cochées de la précédente encaisserait un frais inexistant.
         *
         * Pré-cochage du cas courant au guichet — les frais ponctuels (inscription, tenue, carnet…) et
         * la PREMIÈRE mensualité. C'est ce que règle un tuteur le jour de l'inscription ; tout reste
         * décochable, et le total encaissé est affiché en permanence au-dessus du bouton d'envoi.
         */
        resetCollected() {
            this.collected = {};
            this.previewLines().forEach((line) => {
                this.collected[line.feeCategoryId] = { checked: true, months: 1 };
            });
        },

        collectedEntry(line) {
            return this.collected[line.feeCategoryId] ||= { checked: false, months: 1 };
        },

        /** Montant réellement encaissé pour une ligne : mensualité = unitaire × mois réglés, sinon total. */
        collectedAmount(line) {
            const entry = this.collectedEntry(line);
            if (!entry.checked) return 0;
            const months = Math.min(Math.max(Number(entry.months) || 1, 1), line.months);
            return line.isRecurring ? line.unitAmount * months : line.lineTotal;
        },

        /** Somme encaissée le jour même — le seul montant qui figurera en gras sur le reçu. */
        collectedTotal() {
            return this.previewLines().reduce((sum, line) => sum + this.collectedAmount(line), 0);
        },

        /** Ce qu'on transmet au serveur : les catégories cochées et leur durée, jamais un montant. */
        collectedPayload() {
            return this.previewLines()
                .filter((line) => this.collectedEntry(line).checked)
                .map((line) => ({
                    feeCategoryId: line.feeCategoryId,
                    months: line.isRecurring
                        ? Math.min(Math.max(Number(this.collectedEntry(line).months) || 1, 1), line.months)
                        : 1
                }));
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
                this.error = err.message || 'Erreur lors du chargement des élèves.';
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

        async submit() {
            this.formErrors = {};
            this.isSubmitting = true;

            // Encaissement du jour, commun aux deux modes : une réinscription se règle au guichet
            // exactement comme une première inscription.
            const collection = {
                collectedFees: this.collectedPayload(),
                paymentMethod: this.paymentMethod
            };

            const command = this.mode === 'ReEnrollment'
                ? {
                    type: 'ReEnrollment',
                    classroomId: this.form.classroomId,
                    isRepeating: this.form.isRepeating,
                    studentId: this.form.studentId,
                    ...collection
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
                    ...collection
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
            this.pdfError = null;
            const mode = this.mode;
            this.startNewEnrollment();
            this.mode = mode;
        },

        startNewEnrollment() {
            this.receipt = null;
            this.showReceipt = false;
            this.showConfirmDialog = false;
            this.showPdfModal = false;
            if (this.pdfPreviewUrl) {
                URL.revokeObjectURL(this.pdfPreviewUrl);
                this.pdfPreviewUrl = null;
            }
            this.pdfPreviewTitle = '';
            this.pdfDownloadName = '';
            this.pdfError = null;
            this.form = {
                classroomId: '',
                isRepeating: false,
                fullName: '',
                birthDate: '',
                birthPlace: '',
                gender: 'M',
                guardianName: '',
                guardianPhone: '',
                studentId: ''
            };
            this.studentSearch = '';
            this.formErrors = {};
            this.mode = 'NewEnrollment';
            this.collected = {};
            this.paymentMethod = 'Cash';
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
            // Nettoyer tout ancien Blob URL avant de tenter un nouveau chargement.
            if (this.pdfPreviewUrl) {
                URL.revokeObjectURL(this.pdfPreviewUrl);
                this.pdfPreviewUrl = null;
            }
            this.pdfError = null;
            this.pdfLoadError = false;
            this.pdfPreviewTitle = title || 'Document officiel';
            this.pdfDownloadName = downloadName || 'document.pdf';

            try {
                if (!url || url.includes('undefined') || url.includes('null')) {
                    throw new Error(`L'identifiant ou l'URL du document est invalide (${url}).`);
                }
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
                    throw new Error("Le fichier PDF reçu est vide (0 octet). Veuillez réessayer.");
                }
                const pdfBlob = new Blob([rawBlob], { type: 'application/pdf' });
                this.pdfPreviewUrl = URL.createObjectURL(pdfBlob);
                console.log("PDF Blob URL assigned to iframe:", this.pdfPreviewUrl);
            } catch (err) {
                console.error("Erreur openPdfPreview (Enrollments):", err);
                this.pdfError = err.message || 'Erreur lors du chargement du document.';
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
            const iframe = document.getElementById('enr-pdf-preview-frame');
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

        /**
         * Télécharge/Prévisualise le reçu officiel en PDF via la modale (Ticket JGK-E02 / Axe 4).
         */
        async downloadPdf() {
            if (!this.receipt) return;
            await this.openPdfPreview(
                `/api/v1/enrollments/${this.receipt.enrollmentId}/receipt/pdf`,
                `Reçu d'inscription n° ${this.receiptReference()}`,
                `Recu-${this.receipt.receiptNumber}.pdf`
            );
        },

        /**
         * Télécharge/Prévisualise le certificat d'inscription en PDF via la modale (Ticket JGK-E03 / Axe 2).
         */
        async downloadCertificatePdf() {
            if (!this.receipt) return;
            await this.openPdfPreview(
                `/api/v1/enrollments/${this.receipt.enrollmentId}/certificate/pdf`,
                `Certificat / Attestation d'inscription`,
                `Certificat-Inscription-${this.receipt.matricule}.pdf`
            );
        },

        // ---------------------------------------------------------------- Affichage

        classroomLabel(classroom) {
            return `${classroom.name} — ${classroom.level}`;
        },

        typeLabel(type) {
            return type === 'ReEnrollment' ? 'Réinscription' : 'Nouvelle inscription';
        },

        recurringSuffix(line) {
            return line.isRecurring ? ` (× ${line.months} mois)` : '';
        },

        /**
         * Libellé d'une ligne ENCAISSÉE. Une mensualité porte le nombre de mois réellement réglés
         * (« Mensualité (× 1 mois) ») : c'est vérifiable et jamais faux, là où nommer le mois couvert
         * (« Mensualité d'octobre ») supposerait un échéancier que l'application ne tient pas encore.
         */
        collectedLabel(line) {
            return line.isRecurring ? `${line.designation} (× ${line.months} mois)` : line.designation;
        },

        paymentMethodLabel(method) {
            return {
                Cash: 'Espèces',
                Cheque: 'Chèque',
                Transfer: 'Virement',
                MobileMoney: 'Mobile Money'
            }[method] || '—';
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

        /** FCFA : entiers, séparateur de milliers français. Pas de décimales — la monnaie n'en a pas. */
        formatMoney(amount) {
            if (amount === null || amount === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 }).format(amount) + ' FCFA';
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
