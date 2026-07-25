/**
 * Logique front-end (Alpine.js) pour le module Registre Discipline.
 * Gère l'affichage des sanctions, le modal de création, et l'API.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('disciplineView', () => ({
        records: [],
        isLoading: true,
        
        // Modal de création
        isCreateOpen: false,
        isCreating: false,
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
                toast.error(error.message || "Erreur lors du chargement des sanctions.");
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

        async submitCreate() {
            if (!this.form.studentId || !this.form.date || !this.form.type || !this.form.reason) {
                toast.error("Veuillez remplir tous les champs.");
                return;
            }

            this.isCreating = true;
            try {
                // window.api préfixe déjà /api/v1 ; il lève une erreur normalisée si la requête échoue.
                await api.post('/discipline', this.form);
                toast.success("Sanction ajoutée avec succès.");
                this.isCreateOpen = false;
                this.resetForm();
                await this.loadRecords(); // Rafraîchit le tableau
            } catch (error) {
                console.error("Discipline create error:", error);
                toast.error(error.message || "Erreur lors de l'enregistrement.");
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
        },

        formatDate(dateStr) {
            if (!dateStr) return '';
            return new Date(dateStr).toLocaleDateString('fr-FR', {
                year: 'numeric', month: 'long', day: 'numeric'
            });
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
