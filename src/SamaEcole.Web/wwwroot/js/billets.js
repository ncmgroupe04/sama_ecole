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
        lateArrivals: [],
        isLoading: true,
        printingId: null,

        // Slide-over de création
        isCreateOpen: false,
        isCreating: false,
        students: [],
        form: {
            studentId: '',
            date: new Date().toISOString().split('T')[0],
            minutes: 5,
            reason: ''
        },

        init() {
            this.loadLateArrivals();
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

        async submitCreate() {
            if (!this.form.studentId || !this.form.date || !this.form.minutes || !this.form.reason) {
                toast.error('Veuillez remplir tous les champs.');
                return;
            }

            this.isCreating = true;
            try {
                await api.post('/absences/late-arrivals', {
                    studentId: this.form.studentId,
                    date: this.form.date,
                    minutes: Number(this.form.minutes),
                    reason: this.form.reason
                });
                toast.success('Retard enregistré. Vous pouvez imprimer le billet.');
                this.isCreateOpen = false;
                this.resetForm();
                await this.loadLateArrivals();
            } catch (error) {
                console.error('Late arrival create error:', error);
                toast.error(error.message || "Erreur lors de l'enregistrement.");
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
                reason: ''
            };
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            return new Date(dateStr).toLocaleDateString('fr-FR', {
                year: 'numeric', month: 'long', day: 'numeric'
            });
        }
    }));
});
