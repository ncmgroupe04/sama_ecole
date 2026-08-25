/**
 * Logique front-end (Alpine.js) pour le module Registre Discipline.
 * Gère l'affichage des sanctions, le modal de création, et l'API.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('disciplineView', () => ({
        records: [],
        isLoading: true,
        printingId: null,

        // canView(...) référencé jusqu'ici en vue n'existe QUE dans le scope Alpine du sidebarNav()
        // de _Layout.cshtml (une portée SŒUR de disciplineView(), jamais un ancêtre DOM) : l'appel
        // levait une ReferenceError et Alpine masquait silencieusement le bouton pour tout le monde
        // (x-show retombe sur false quand l'expression échoue). Rôles alignés sur DisciplineController
        // (SuperAdmin/Directeur/Surveillant) — le Secrétariat n'a jamais pu créer de sanction côté API.
        canManageDiscipline: window.auth.role === 'Directeur' || window.auth.role === 'Surveillant',

        // Modale de création
        isCreateOpen: false,
        isCreating: false,
        createErrors: {},
        showAddedDialog: false,
        addedRecordStudentName: '',
        students: [], // Liste pour le menu déroulant
        form: {
            studentId: '',
            date: new Date().toISOString().split('T')[0],
            type: 'Avertissement', // Valeur par défaut
            reason: ''
        },

        init() {
            this.loadRecords();
            this.loadStudents();
        },

        async loadRecords() {
            this.isLoading = true;
            try {
                // window.api préfixe déjà /api/v1 et renvoie le JSON désérialisé (ou lève une erreur).
                this.records = await api.get('/discipline');
            } catch (error) {
                console.error("Discipline fetch error:", error);
                toast.error(window.api.toMessage(error, "Erreur lors du chargement des sanctions."));
            } finally {
                this.isLoading = false;
            }
        },

        async loadStudents() {
            try {
                // On récupère une large liste pour le menu déroulant
                const data = await api.get('/students?page=1&pageSize=1000');
                this.students = (data && data.items) || [];
            } catch (error) {
                console.error("Erreur chargement élèves:", error);
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
                // window.api préfixe déjà /api/v1 ; il lève une erreur normalisée si la requête échoue.
                await api.post('/discipline', this.form);

                this.isCreateOpen = false;
                this.addedRecordStudentName = student ? student.fullName : '';
                this.resetForm();
                await this.loadRecords(); // Rafraîchit le tableau
                this.showAddedDialog = true;
            } catch (error) {
                console.error("Discipline create error:", error);
                this.createErrors = window.api.toFieldErrors(error, "Erreur lors de l'enregistrement.");
            } finally {
                this.isCreating = false;
            }
        },

        resetForm() {
            this.form = {
                studentId: '',
                date: new Date().toISOString().split('T')[0],
                type: 'Avertissement',
                reason: ''
            };
            this.createErrors = {};
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            return new Date(dateStr).toLocaleDateString('fr-FR', {
                year: 'numeric', month: 'long', day: 'numeric'
            });
        },

        /**
         * PV de discipline en PDF, ouvert dans un nouvel onglet (même mécanique que
         * billets.js printBillet) : le jeton ne voyage pas sur une navigation classique.
         */
        async printPv(recordId) {
            if (!recordId || recordId === 'undefined') {
                console.error('Identifiant de dossier de discipline invalide ou indéfini', recordId);
                return;
            }
            this.printingId = recordId;
            try {
                const response = await fetch(`/api/v1/discipline/${recordId}/pv/pdf`, {
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
                    link.download = `PV-Discipline-${recordId}.pdf`;
                    link.click();
                }
                setTimeout(() => URL.revokeObjectURL(url), 60000);
            } catch (error) {
                console.error('PV discipline print error:', error);
                toast.error(window.api.toMessage(error, "Erreur lors de la génération du PV."));
            } finally {
                this.printingId = null;
            }
        },

        getDisciplineBadge(type) {
            const badges = {
                'Avertissement': { label: 'Avertissement', css: 'bg-yellow-100 text-yellow-800' },
                'Blame': { label: 'Blâme', css: 'bg-orange-100 text-orange-800' },
                'Retenue': { label: 'Retenue', css: 'bg-blue-100 text-blue-800' },
                'Exclusion': { label: 'Exclusion', css: 'bg-red-100 text-red-800' }
            };
            return badges[type] || { label: type, css: 'bg-slate-100 text-slate-800' };
        }
    }));
});
