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
            arrivalTime: ''
        },

        // Billet par HEURE D'ARRIVÉE (Complément N°5 bis). Quand la classe de l'élève a des cours ce jour-là, la
        // Surveillance ne saisit que l'heure d'arrivée réelle : le SERVEUR en déduit les cours manqués, le retard sur
        // le cours en cours, le cours visé et la durée totale. L'écran n'affiche que l'aperçu que le serveur calcule
        // (GET /absences/arrival-preview) — aucune règle de calcul n'est recopiée ici. Sans cours ce jour-là (repos,
        // pas d'emploi du temps), l'ancien champ « Minutes de retard » reste seul.
        todaySlots: [],
        isLoadingSlots: false,
        arrivalPreview: null,
        arrivalError: '',
        isLoadingPreview: false,
        previewSeq: 0,
        previewTimer: null,

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
                this.$watch('form.arrivalTime', () => this.scheduleArrivalPreview());
            }
        },

        /** Vrai quand la classe de l'élève a des cours à la date choisie : l'heure d'arrivée remplace les minutes. */
        get hasSlots() {
            return this.todaySlots.length > 0;
        },

        /** « Enregistrer » : avec des cours, il faut une heure d'arrivée dont l'aperçu serveur est valide. */
        get canSubmitCreate() {
            if (this.isCreating) return false;
            if (!this.hasSlots) return true;
            return !!this.arrivalPreview && !this.arrivalError && !this.isLoadingPreview;
        },

        /** Les cours de la classe de l'élève à la date du retard : de quoi savoir quel champ proposer. */
        async loadTodaySlots() {
            this.todaySlots = [];
            this.resetPreview();
            this.form.arrivalTime = '';
            if (!this.form.studentId || !this.form.date) return;

            this.isLoadingSlots = true;
            try {
                const params = new URLSearchParams({ studentId: this.form.studentId, date: this.form.date });
                const data = await api.get(`/absences/today-slots?${params.toString()}`);
                this.todaySlots = Array.isArray(data) ? data : [];
            } catch (error) {
                console.error('Today slots fetch error:', error);
                this.todaySlots = [];
                toast.error(window.api.toMessage(error, 'Erreur lors du chargement des cours du jour.'));
            } finally {
                this.isLoadingSlots = false;
            }
        },

        resetPreview() {
            clearTimeout(this.previewTimer);
            this.previewSeq += 1; // toute réponse encore en vol devient périmée
            this.arrivalPreview = null;
            this.arrivalError = '';
            this.isLoadingPreview = false;
        },

        /** Laisse finir la frappe avant d'interroger le serveur. */
        scheduleArrivalPreview() {
            clearTimeout(this.previewTimer);
            this.arrivalPreview = null;
            this.arrivalError = '';
            if (!this.hasSlots || !this.form.arrivalTime) return;

            this.previewTimer = setTimeout(() => this.loadArrivalPreview(), 300);
        },

        /** Ce que le billet va régulariser pour cette heure d'arrivée — calculé par le serveur, comme à l'émission. */
        async loadArrivalPreview() {
            clearTimeout(this.previewTimer);
            this.arrivalPreview = null;
            this.arrivalError = '';
            if (!this.hasSlots || !this.form.studentId || !this.form.date || !this.form.arrivalTime) return;

            const seq = ++this.previewSeq;
            this.isLoadingPreview = true;
            try {
                const params = new URLSearchParams({
                    studentId: this.form.studentId,
                    date: this.form.date,
                    arrivalTime: this.serverTime(this.form.arrivalTime)
                });
                const data = await api.get(`/absences/arrival-preview?${params.toString()}`);
                if (seq === this.previewSeq) this.arrivalPreview = data;
            } catch (error) {
                if (seq !== this.previewSeq) return;
                // 422 : arrivée avant le premier cours, rien à régulariser, cours déjà couverts, jour de repos…
                const fallback = "Impossible de calculer le billet pour cette heure d'arrivée.";
                const fields = window.api.toFieldErrors(error, fallback);
                this.arrivalError = fields.arrivaltime || fields.date || fields.global || fallback;
            } finally {
                if (seq === this.previewSeq) this.isLoadingPreview = false;
            }
        },

        /** « 10:20 » (champ heure) → « 10:20:00 » : le format qu'attend l'API (comme l'emploi du temps). */
        serverTime(hhmm) {
            return /^\d{2}:\d{2}$/.test(hhmm) ? `${hhmm}:00` : hhmm;
        },

        /** 140 → « 2 h 20 » ; 45 → « 45 min » : mise en forme d'un nombre DÉJÀ calculé par le serveur. */
        formatDuration(minutes) {
            const m = Number(minutes) || 0;
            if (m < 60) return `${m} min`;
            const rest = m % 60;
            return rest === 0 ? `${Math.floor(m / 60)} h` : `${Math.floor(m / 60)} h ${String(rest).padStart(2, '0')}`;
        },

        /** Colonne « Retard » de la liste : l'arrivée et la durée régularisée pour un billet par heure d'arrivée. */
        lateCellText(item) {
            if (!item.arrivalTime) return `${item.minutes} min`;
            const hhmm = String(item.arrivalTime).slice(0, 5);
            return `Arrivé à ${hhmm} · ${this.formatDuration(item.totalMinutes)}`;
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
                const payload = {
                    studentId: this.form.studentId,
                    date: this.form.date,
                    reason: this.form.reason,
                    observations: this.form.observations
                };
                // Avec des cours ce jour-là : l'heure d'arrivée SEULE — minutes et cours visé sont déduits (et, de
                // toute façon, ignorés) par le serveur. Sans cours : les minutes saisies, comme avant.
                if (this.hasSlots) payload.arrivalTime = this.serverTime(this.form.arrivalTime);
                else payload.minutes = Number(this.form.minutes);

                await api.post('/absences/late-arrivals', payload);

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
                arrivalTime: ''
            };
            this.todaySlots = [];
            this.resetPreview();
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
