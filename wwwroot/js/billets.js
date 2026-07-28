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
            observations: ''
        },

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
        },

        async loadLateArrivals() {
            this.isLoading = true;
            try {
                this.lateArrivals = await api.get('/absences/late-arrivals');
            } catch (error) {
                console.error('Late arrivals fetch error:', error);
                toast.error(error.message || 'Erreur lors du chargement des retards.');
            } finally {
                this.isLoading = false;
            }
        },

        async loadStudents() {
            try {
                const data = await api.get('/students?page=1&pageSize=1000');
                this.students = (data && data.items) || [];
            } catch (error) {
                console.error('Erreur chargement élèves:', error);
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
                    observations: this.form.observations
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
         * Récupère le billet A5 en PDF et l'ouvre dans un nouvel onglet (aperçu + impression).
         * Le PDF n'est pas du JSON : fetch brut avec le jeton, puis blob URL — même mécanique que la
         * Caisse (le jeton ne voyage pas sur une navigation classique).
         */
        async printBillet(lateArrivalId) {
            if (!lateArrivalId || lateArrivalId === 'undefined') {
                console.error('Identifiant de billet invalide ou indéfini', lateArrivalId);
                return;
            }
            this.printingId = lateArrivalId;
            try {
                const response = await fetch(`/api/v1/billets/late-arrival/${lateArrivalId}/pdf`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` }
                });
                if (!response.ok) {
                    throw new Error(`Le serveur a renvoyé ${response.status}.`);
                }
                const blob = new Blob([await response.blob()], { type: 'application/pdf' });
                const url = URL.createObjectURL(blob);
                const win = window.open(url, '_blank');
                if (!win) {
                    // Bloqueur de pop-up : on retombe sur un téléchargement.
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `Billet-${lateArrivalId}.pdf`;
                    link.click();
                }
                // Libère l'URL une fois le document chargé (délai large pour l'onglet).
                setTimeout(() => URL.revokeObjectURL(url), 60000);
            } catch (error) {
                console.error('Billet print error:', error);
                toast.error(error.message || "Erreur lors de la génération du billet.");
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
                observations: ''
            };
            this.createErrors = {};
        },

        // ---------------------------------------------------------------- Billets de sortie (EarlyDeparture)

        async loadEarlyDepartures() {
            this.isLoadingExits = true;
            try {
                this.earlyDepartures = await api.get('/absences/early-departures');
            } catch (error) {
                console.error('Early departures fetch error:', error);
                toast.error(error.message || 'Erreur lors du chargement des sorties.');
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

        /** Billet de sortie A5 en PDF — même mécanique que printBillet. */
        async printExitBillet(earlyDepartureId) {
            if (!earlyDepartureId || earlyDepartureId === 'undefined') {
                console.error('Identifiant de billet de sortie invalide ou indéfini', earlyDepartureId);
                return;
            }
            this.printingExitId = earlyDepartureId;
            try {
                const response = await fetch(`/api/v1/billets/early-departure/${earlyDepartureId}/pdf`, {
                    headers: { Authorization: `Bearer ${window.auth.accessToken}` }
                });
                if (!response.ok) {
                    throw new Error(`Le serveur a renvoyé ${response.status}.`);
                }
                const blob = new Blob([await response.blob()], { type: 'application/pdf' });
                const url = URL.createObjectURL(blob);
                const win = window.open(url, '_blank');
                if (!win) {
                    const link = document.createElement('a');
                    link.href = url;
                    link.download = `Billet-Sortie-${earlyDepartureId}.pdf`;
                    link.click();
                }
                setTimeout(() => URL.revokeObjectURL(url), 60000);
            } catch (error) {
                console.error('Exit billet print error:', error);
                toast.error(error.message || "Erreur lors de la génération du billet.");
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
