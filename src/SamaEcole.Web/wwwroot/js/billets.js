/**
 * Logique front-end (Alpine.js) pour le module Billets d'entrée (Surveillance).
 *
 * Un billet d'entrée est l'impression A5 d'un retard enregistré : on saisit le retard d'un élève,
 * puis on imprime le billet correspondant, remis à l'élève pour rejoindre sa classe.
 *
 * NB : window.api.get/post renvoient DÉJÀ le JSON désérialisé (ou lèvent une erreur normalisée) — il
 * ne faut donc PAS tester response.ok ni appeler response.json(). Le PDF, lui, n'est pas du JSON :
 * on le récupère par un fetch brut portant le jeton, puis on l'ouvre en blob (même approche que la
 * Caisse).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('billetsView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : le billet s'ouvre dans la modale
        // _PdfPreviewModal (impression / téléchargement au choix), jamais un download forcé.
        ...window.pdfPreview.state(),

        tab: 'entree',
        lateArrivals: [],
        isLoading: true,
        printingId: null,

        // canView(...) référencé jusqu'ici en vue n'existe QUE dans le scope Alpine du sidebarNav()
        // de _Layout.cshtml (une portée SŒUR de billetsView(), jamais un ancêtre DOM) : l'appel levait
        // une ReferenceError et Alpine masquait silencieusement les deux boutons pour tout le monde.
        // Rôles alignés sur AbsenceController, qui gouverne les DEUX créations (retard/sortie) :
        // SuperAdmin/Directeur/Surveillant — le Secrétariat imprime le billet (BilletsController) mais
        // ne crée pas le retard/la sortie lui-même.
        canManageBillets: window.auth.role === 'Directeur' || window.auth.role === 'Surveillant',

        // Modale de création (billet d'entrée)
        isCreateOpen: false,
        isCreating: false,
        createErrors: {},
        showAddedDialog: false,
        addedLateArrivalName: '',
        students: [],
        form: {
            studentId: '',
            date: new Date().toISOString().split('T')[0],
            minutes: 5,
            reason: '',
            observations: '',
            targetScheduleSlotId: ''
        },

        // Cours visé par le billet (Évolution N°5) : les cours du jour de la classe de l'élève. Le cours en cours
        // (à défaut le prochain) est présélectionné ; « Sans cours précis » reste possible et produit un billet
        // comme avant. Confort d'affichage : le serveur revérifie que le cours est bien celui de la classe, ce jour-là.
        todaySlots: [],
        isLoadingSlots: false,

        // Annulation d'un billet en attente (Vie Scolaire / Directeur) — jamais sans confirmation.
        ticketToCancel: null,
        isCancelling: false,
        cancelError: '',

        // ---------------------------------------------------------------- Billets de sortie (EarlyDeparture)
        earlyDepartures: [],
        isLoadingExits: true,
        printingExitId: null,
        isCreateExitOpen: false,
        isCreatingExit: false,
        createExitErrors: {},
        showAddedExitDialog: false,
        addedEarlyDepartureName: '',
        exitForm: {
            studentId: '',
            date: new Date().toISOString().split('T')[0],
            departureTime: '',
            reason: '',
            pickedUpBy: ''
        },

        init() {
            this.loadLateArrivals();
            this.loadEarlyDepartures();
            this.loadStudents();

            // $watch n'existe pas hors d'Alpine (tests) : loadTodaySlots reste appelable directement.
            if (typeof this.$watch === 'function') {
                this.$watch('form.studentId', () => this.loadTodaySlots());
                this.$watch('form.date', () => this.loadTodaySlots());
            }
        },

        /** Les cours de la classe de l'élève à la date du retard ; présélectionne le cours en cours, sinon le prochain. */
        async loadTodaySlots() {
            this.todaySlots = [];
            this.form.targetScheduleSlotId = '';
            if (!this.form.studentId || !this.form.date) return;

            this.isLoadingSlots = true;
            try {
                const params = new URLSearchParams({ studentId: this.form.studentId, date: this.form.date });
                const data = await api.get(`/absences/today-slots?${params.toString()}`);
                this.todaySlots = Array.isArray(data) ? data : [];
                const preselected = this.todaySlots.find((s) => s.isCurrent) || this.todaySlots.find((s) => s.isNext);
                this.form.targetScheduleSlotId = preselected ? preselected.slotId : '';
            } catch (error) {
                console.error('Today slots fetch error:', error);
                this.todaySlots = [];
                toast.error(window.api.toMessage(error, 'Erreur lors du chargement des cours du jour.'));
            } finally {
                this.isLoadingSlots = false;
            }
        },

        /** Libellé d'un cours dans le sélecteur : « 08:00-10:00 · Mathématiques (Awa Sow) ». */
        slotOptionLabel(slot) {
            const marker = slot.isCurrent ? ' — en cours' : (slot.isNext ? ' — prochain' : '');
            const taken = slot.ticketStatus ? ' — billet déjà émis' : '';
            return `${slot.label} · ${slot.subjectName} (${slot.teacherName})${marker}${taken}`;
        },

        // ---------------------------------------------------------------- Statut et annulation du billet d'entrée

        ticketStatusLabel(status) {
            switch (status) {
                case 'Issued': return 'En attente d\'acceptation';
                case 'Accepted': return 'Accepté en classe';
                case 'Cancelled': return 'Annulé';
                default: return '';
            }
        },

        ticketStatusBadge(status) {
            switch (status) {
                case 'Issued': return 'status-badge-warning';
                case 'Accepted': return 'status-badge-success';
                case 'Cancelled': return 'status-badge-danger';
                default: return 'status-badge-neutral';
            }
        },

        /** Annulable tant qu'il est en attente ; le serveur refuse un billet déjà accepté (422) et tout autre rôle (403). */
        canCancelTicket(item) {
            return this.canManageBillets && item.status === 'Issued';
        },

        askCancelTicket(item) {
            this.cancelError = '';
            this.ticketToCancel = item;
        },

        closeCancel() {
            this.ticketToCancel = null;
            this.cancelError = '';
        },

        async confirmCancelTicket() {
            if (!this.ticketToCancel) return;

            this.isCancelling = true;
            this.cancelError = '';
            try {
                await api.post(`/billets/${this.ticketToCancel.id}/cancel`, {});
                this.ticketToCancel = null;
                await this.loadLateArrivals();
                toast.success('Billet annulé.');
            } catch (error) {
                console.error('Ticket cancel error:', error);
                this.cancelError = window.api.toMessage(error, "Erreur lors de l'annulation du billet.");
            } finally {
                this.isCancelling = false;
            }
        },

        async loadLateArrivals() {
            this.isLoading = true;
            try {
                this.lateArrivals = await api.get('/absences/late-arrivals');
            } catch (error) {
                console.error('Late arrivals fetch error:', error);
                toast.error(window.api.toMessage(error, 'Erreur lors du chargement des retards.'));
            } finally {
                this.isLoading = false;
            }
        },

        async loadStudents() {
            try {
                // Toutes les pages : le serveur plafonne pageSize à 100 (GetStudentsQueryValidator).
                // Un `pageSize=1000` partait en 422 et laissait le sélecteur VIDE sans message —
                // les billets d'entrée et de sortie étaient inutilisables (voir api.getAllPages).
                this.students = await api.getAllPages('/students');
            } catch (error) {
                console.error('Erreur chargement élèves:', error);
                this.students = [];
                // Sans ce toast, l'échec est invisible : un menu vide ressemble à « aucun élève ».
                toast.error(window.api.toMessage(error, 'Erreur lors du chargement des élèves.'));
            }
        },

        openCreate() {
            this.isCreateOpen = true;
        },

        /** Sortie explicite (Annuler, ✕, fond, Échap) : le formulaire repart de zéro. */
        closeCreate() {
            this.isCreateOpen = false;
            this.resetForm();
        },

        async submitCreate() {
            this.isCreating = true;
            this.createErrors = {};
            try {
                const student = this.students.find(s => s.id === this.form.studentId);
                await api.post('/absences/late-arrivals', {
                    studentId: this.form.studentId,
                    date: this.form.date,
                    minutes: Number(this.form.minutes),
                    reason: this.form.reason,
                    observations: this.form.observations,
                    // Cours visé (Évolution N°5) : null = billet sans cours précis, comme avant.
                    targetScheduleSlotId: this.form.targetScheduleSlotId || null
                });

                this.isCreateOpen = false;
                this.addedLateArrivalName = student ? student.fullName : '';
                this.resetForm();
                await this.loadLateArrivals();
                this.showAddedDialog = true;
            } catch (error) {
                console.error('Late arrival create error:', error);
                this.createErrors = window.api.toFieldErrors(error, "Erreur lors de l'enregistrement.");
            } finally {
                this.isCreating = false;
            }
        },

        /**
         * Ouvre le billet A5 en PDF dans la modale d'aperçu partagée (pdf-preview.js) : l'utilisateur
         * le relit puis imprime ou télécharge depuis l'en-tête de la modale. Le jeton voyage en
         * en-tête Authorization (géré par openPdfPreview), jamais sur une navigation classique.
         */
        async printBillet(lateArrivalId) {
            if (!lateArrivalId || lateArrivalId === 'undefined') {
                console.error('Identifiant de billet invalide ou indéfini', lateArrivalId);
                return;
            }
            this.printingId = lateArrivalId;
            try {
                await this.openPdfPreview(
                    `/api/v1/billets/late-arrival/${lateArrivalId}/pdf`,
                    "Billet d'entrée",
                    `Billet-${lateArrivalId}.pdf`
                );
            } finally {
                this.printingId = null;
            }
        },

        resetForm() {
            this.form = {
                studentId: '',
                date: new Date().toISOString().split('T')[0],
                minutes: 5,
                reason: '',
                observations: '',
                targetScheduleSlotId: ''
            };
            this.todaySlots = [];
            this.createErrors = {};
        },

        // ---------------------------------------------------------------- Billets de sortie (EarlyDeparture)

        async loadEarlyDepartures() {
            this.isLoadingExits = true;
            try {
                this.earlyDepartures = await api.get('/absences/early-departures');
            } catch (error) {
                console.error('Early departures fetch error:', error);
                toast.error(window.api.toMessage(error, 'Erreur lors du chargement des sorties.'));
            } finally {
                this.isLoadingExits = false;
            }
        },

        openCreateExit() {
            this.isCreateExitOpen = true;
        },

        /** Sortie explicite (Annuler, ✕, fond, Échap) : le formulaire repart de zéro. */
        closeCreateExit() {
            this.isCreateExitOpen = false;
            this.resetExitForm();
        },

        async submitCreateExit() {
            this.isCreatingExit = true;
            this.createExitErrors = {};
            try {
                const student = this.students.find(s => s.id === this.exitForm.studentId);
                await api.post('/absences/early-departures', {
                    studentId: this.exitForm.studentId,
                    date: this.exitForm.date,
                    departureTime: this.exitForm.departureTime,
                    reason: this.exitForm.reason,
                    pickedUpBy: this.exitForm.pickedUpBy || null
                });

                this.isCreateExitOpen = false;
                this.addedEarlyDepartureName = student ? student.fullName : '';
                this.resetExitForm();
                await this.loadEarlyDepartures();
                this.showAddedExitDialog = true;
            } catch (error) {
                console.error('Early departure create error:', error);
                this.createExitErrors = window.api.toFieldErrors(error, "Erreur lors de l'enregistrement.");
            } finally {
                this.isCreatingExit = false;
            }
        },

        /** Billet de sortie A5 en PDF — même mécanique que printBillet (modale d'aperçu partagée). */
        async printExitBillet(earlyDepartureId) {
            if (!earlyDepartureId || earlyDepartureId === 'undefined') {
                console.error('Identifiant de billet de sortie invalide ou indéfini', earlyDepartureId);
                return;
            }
            this.printingExitId = earlyDepartureId;
            try {
                await this.openPdfPreview(
                    `/api/v1/billets/early-departure/${earlyDepartureId}/pdf`,
                    'Billet de sortie',
                    `Billet-Sortie-${earlyDepartureId}.pdf`
                );
            } finally {
                this.printingExitId = null;
            }
        },

        resetExitForm() {
            this.exitForm = {
                studentId: '',
                date: new Date().toISOString().split('T')[0],
                departureTime: '',
                reason: '',
                pickedUpBy: ''
            };
            this.createExitErrors = {};
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            return new Date(dateStr).toLocaleDateString('fr-FR', {
                year: 'numeric', month: 'long', day: 'numeric'
            });
        }
    }));
});
