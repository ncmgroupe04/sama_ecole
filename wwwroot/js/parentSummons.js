/**
 * Logique front-end (Alpine.js) pour le module Convocations parent (module Vie Scolaire).
 * Une convocation est un motif libre de rencontre avec le parent/tuteur, distinct d'une sanction
 * disciplinaire (voir discipline.js) — même structure : liste, slide-over de création, impression PDF.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('parentSummonsView', () => ({
        records: [],
        isLoading: true,
        printingId: null,

        // Rôles alignés sur ParentSummonsController (SuperAdmin/Directeur/Surveillant).
        canManageParentSummons: window.auth.role === 'Directeur' || window.auth.role === 'Surveillant',

        // Modale de création
        isCreateOpen: false,
        isCreating: false,
        createErrors: {},
        showAddedDialog: false,
        addedRecordStudentName: '',
        students: [],
        form: {
            studentId: '',
            scheduledDate: new Date().toISOString().split('T')[0],
            scheduledTime: '',
            reason: ''
        },

        init() {
            this.loadRecords();
            this.loadStudents();
        },

        async loadRecords() {
            this.isLoading = true;
            try {
                this.records = await api.get('/parent-summons');
            } catch (error) {
                console.error('Parent summons fetch error:', error);
                toast.error(error.message || 'Erreur lors du chargement des convocations.');
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
                await api.post('/parent-summons', {
                    studentId: this.form.studentId,
                    scheduledAt: new Date(`${this.form.scheduledDate}T${this.form.scheduledTime}`).toISOString(),
                    reason: this.form.reason
                });

                this.isCreateOpen = false;
                this.addedRecordStudentName = student ? student.fullName : '';
                this.resetForm();
                await this.loadRecords();
                this.showAddedDialog = true;
            } catch (error) {
                console.error('Parent summons create error:', error);
                this.createErrors = window.api.toFieldErrors(error, "Erreur lors de l'enregistrement.");
            } finally {
                this.isCreating = false;
            }
        },

        /** Convocation PDF, ouverte dans un nouvel onglet — même mécanique que discipline.js printPv. */
        async printNotice(recordId) {
            if (!recordId || recordId === 'undefined') {
                console.error('Identifiant de convocation invalide ou indéfini', recordId);
                return;
            }
            this.printingId = recordId;
            try {
                const response = await fetch(`/api/v1/parent-summons/${recordId}/notice/pdf`, {
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
                    link.download = `Convocation-${recordId}.pdf`;
                    link.click();
                }
                setTimeout(() => URL.revokeObjectURL(url), 60000);
            } catch (error) {
                console.error('Parent notice print error:', error);
                toast.error(error.message || 'Erreur lors de la génération de la convocation.');
            } finally {
                this.printingId = null;
            }
        },

        resetForm() {
            this.form = {
                studentId: '',
                scheduledDate: new Date().toISOString().split('T')[0],
                scheduledTime: '',
                reason: ''
            };
            this.createErrors = {};
        },

        formatDateTime(dateStr) {
            if (!dateStr) return '';
            return new Date(dateStr).toLocaleString('fr-FR', {
                year: 'numeric', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit'
            });
        }
    }));
});
