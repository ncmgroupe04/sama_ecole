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
            fullName: '',
            birthDate: '',
            gender: 'M',
            guardianName: '',
            guardianPhone: '',
            studentId: ''
        },
        formErrors: {},
        isSubmitting: false,

        // Reçu émis
        receipt: null,
        pdfError: null,

        // Parcours après enregistrement : une fois l'inscription validée, on affiche d'abord une
        // fenêtre de confirmation (« Inscription validée »), et le reçu ne s'affiche que si
        // l'utilisateur choisit de l'imprimer/consulter — il n'est plus jeté directement à l'écran.
        showConfirmDialog: false,
        showReceipt: false,

        async init() {
            await this.loadReferenceData();
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

            const command = this.mode === 'ReEnrollment'
                ? {
                    type: 'ReEnrollment',
                    classroomId: this.form.classroomId,
                    studentId: this.form.studentId
                }
                : {
                    type: 'NewEnrollment',
                    classroomId: this.form.classroomId,
                    fullName: this.form.fullName,
                    birthDate: this.form.birthDate || null,
                    gender: this.form.gender,
                    guardianName: this.form.guardianName || null,
                    guardianPhone: this.form.guardianPhone || null
                };

            try {
                this.receipt = await window.api.post('/enrollments', command);
                // Étape 1 : on confirme l'enregistrement dans une fenêtre dédiée. Le reçu n'apparaît
                // qu'ensuite, si l'utilisateur clique « Imprimer le reçu ».
                this.showReceipt = false;
                this.showConfirmDialog = true;
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
            this.pdfError = null;
            this.form = {
                classroomId: '',
                fullName: '',
                birthDate: '',
                gender: 'M',
                guardianName: '',
                guardianPhone: '',
                studentId: ''
            };
            this.studentSearch = '';
            this.formErrors = {};
            this.mode = 'NewEnrollment';
        },

        printReceipt() {
            window.print();
        },

        /**
         * Télécharge le reçu officiel en PDF (ticket JGK-E02). L'API exige le jeton : un simple lien ne
         * suffit pas, on récupère donc le PDF en blob avec l'en-tête Authorization, puis on déclenche le
         * téléchargement côté navigateur. Renouvellement préventif du jeton, comme window.api.
         */
        async downloadPdf() {
            if (!this.receipt) return;
            this.pdfError = null;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch(`/api/v1/enrollments/${this.receipt.enrollmentId}/receipt/pdf`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` },
                    credentials: 'same-origin'
                });
                if (!response.ok) throw new Error('Téléchargement du reçu impossible.');

                const blob = await response.blob();
                const url = URL.createObjectURL(blob);
                const link = document.createElement('a');
                link.href = url;
                link.download = `Recu-${this.receipt.receiptNumber}.pdf`;
                document.body.appendChild(link);
                link.click();
                link.remove();
                URL.revokeObjectURL(url);
            } catch (err) {
                this.pdfError = err.message || 'Téléchargement du reçu impossible.';
            }
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
