/**
 * Logique front-end (Alpine.js) pour le module Convocations parent (module Vie Scolaire).
 * Une convocation est un motif libre de rencontre avec le parent/tuteur, distinct d'une sanction
 * disciplinaire (voir discipline.js) — même structure : liste, slide-over de création, impression PDF.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('parentSummonsView', () => ({
        // Aperçu PDF partagé (wwwroot/js/pdf-preview.js) : la convocation s'ouvre dans la modale
        // _PdfPreviewModal (impression / téléchargement au choix), jamais un download forcé.
        ...window.pdfPreview.state(),

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

        // ------------------------------------------------------- Suite de l'entretien (02/09/2026)
        //
        // Une convocation sans suite est un rendez-vous, pas un entretien : le registre listait des
        // dates sans jamais dire si le parent était venu. La suite se consigne UNE FOIS, depuis
        // « Planifiée » — le serveur (CloseParentSummonsCommandHandler) refuse la seconde en 422,
        // ce formulaire ne fait que ne pas la proposer.
        isOutcomeOpen: false,
        isClosingOutcome: false,
        outcomeErrors: {},
        outcomeTarget: null,
        outcomeForm: { outcome: '', outcomeNotes: '' },

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
                toast.error(window.api.toMessage(error, 'Erreur lors du chargement des convocations.'));
            } finally {
                this.isLoading = false;
            }
        },

        async loadStudents() {
            try {
                // Toutes les pages : le serveur plafonne pageSize à 100 (GetStudentsQueryValidator).
                // Un `pageSize=1000` partait en 422 et laissait le sélecteur VIDE sans message —
                // aucune convocation de parent ne pouvait être créée (voir api.getAllPages).
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

        openOutcome(record) {
            this.outcomeTarget = record;
            this.outcomeForm = { outcome: '', outcomeNotes: '' };
            this.outcomeErrors = {};
            this.isOutcomeOpen = true;
        },

        closeOutcome() {
            this.isOutcomeOpen = false;
            this.outcomeTarget = null;
            this.outcomeForm = { outcome: '', outcomeNotes: '' };
            this.outcomeErrors = {};
        },

        /**
         * Compte rendu obligatoire dès que l'entretien n'a PAS eu lieu comme prévu. Même règle que le
         * Handler : « Honorée » se suffit à elle-même, une absence ou un report appellent une suite.
         * Reproduite ici pour l'astérisque et le texte d'aide, jamais pour remplacer la garde serveur.
         */
        outcomeNotesRequired() {
            return this.outcomeForm.outcome === 'Missed' || this.outcomeForm.outcome === 'Postponed';
        },

        outcomeNotesPlaceholder() {
            switch (this.outcomeForm.outcome) {
                case 'Missed': return 'Suite donnée à cette absence : relance, nouvelle convocation…';
                case 'Postponed': return 'Motif du report et suite prévue…';
                case 'Honored': return "Ce qui s'est dit pendant l'entretien, engagements pris… (facultatif)";
                default: return 'Compte rendu de l\'entretien…';
            }
        },

        async submitOutcome() {
            if (!this.outcomeTarget) return;

            this.isClosingOutcome = true;
            this.outcomeErrors = {};
            try {
                await api.patch(`/parent-summons/${this.outcomeTarget.id}/outcome`, {
                    outcome: this.outcomeForm.outcome,
                    outcomeNotes: this.outcomeForm.outcomeNotes || null
                });
                this.closeOutcome();
                // Relecture plutôt que mise à jour locale : le tri place les convocations SANS suite
                // en tête, celle qu'on vient de clore doit descendre — recopier le statut sur place
                // la laisserait en haut de liste, à contre-emploi de l'écran.
                await this.loadRecords();
                toast.success('Suite consignée.');
            } catch (error) {
                this.outcomeErrors = window.api.toFieldErrors(error, "Erreur lors de l'enregistrement de la suite.");
            } finally {
                this.isClosingOutcome = false;
            }
        },

        /** Libellé français d'un ParentSummonsStatus (l'API renvoie le nom anglais de l'enum). */
        outcomeLabel(status) {
            switch (status) {
                case 'Honored': return 'Honorée';
                case 'Missed': return 'Non honorée';
                case 'Postponed': return 'Reportée';
                case 'Scheduled': return 'Planifiée';
                default: return status || '—';
            }
        },

        outcomeBadgeClass(status) {
            switch (status) {
                case 'Honored': return 'status-badge-success';
                case 'Missed': return 'status-badge-danger';
                case 'Postponed': return 'status-badge-warning';
                case 'Scheduled': return 'status-badge-primary';
                default: return 'status-badge-neutral';
            }
        },

        /** Convocation PDF, ouverte dans la modale d'aperçu partagée — même mécanique que discipline.js printPv. */
        async printNotice(recordId) {
            if (!recordId || recordId === 'undefined') {
                console.error('Identifiant de convocation invalide ou indéfini', recordId);
                return;
            }
            this.printingId = recordId;
            try {
                await this.openPdfPreview(
                    `/api/v1/parent-summons/${recordId}/notice/pdf`,
                    'Convocation parent',
                    `Convocation-${recordId}.pdf`
                );
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
