/**
 * Cahier de texte / journal de classe (/cahier-de-texte, ticket JGK-P04) — une entrée par séance
 * réellement tenue, saisie par l'enseignant titulaire du créneau.
 *
 * Matrice de droits reproduite ICI en confort d'affichage — la garde réelle est
 * ClassJournalController/ClassJournalScopeAuthorizer/ClassJournalEditWindow :
 * - LECTURE : Directeur, Secrétariat, Surveillant, Enseignant — document pédagogique partagé,
 *   pas un carnet privé par enseignant.
 * - CRÉATION : Enseignant seul, et seulement pour sa propre séance planifiée (409 sinon).
 * - CORRECTION (modifier/supprimer) : l'auteur dans les 15 jours suivant la séance, ou
 *   Directeur/Secrétariat au-delà (403 sinon) — canEditEntry() ci-dessous n'est qu'un calcul
 *   d'affichage, jamais la vraie garde.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('classJournalView', () => ({
        isDirecteurOuSecretariat: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat',
        isEnseignant: window.auth.role === 'Enseignant',
        error: null,

        // ---------------------------------------------------------------- Données de référence
        classrooms: [],
        subjects: [],

        get classroomOptions() {
            return this.classrooms.map((c) => ({ value: c.id, label: c.name }));
        },

        get subjectOptions() {
            return this.subjects.map((s) => ({ value: s.id, label: s.name }));
        },

        /** DateOnly (« 2026-06-15 ») → jj/mm/aaaa, sans passer par un Date() qui déraperait d'un jour selon le fuseau. */
        formatDate(isoDate) {
            if (!isoDate) return '—';
            const [year, month, day] = isoDate.split('-');
            return `${day}/${month}/${year}`;
        },

        /**
         * `entry.canEdit` est calculé CÔTÉ SERVEUR par GetClassJournalQueryHandler
         * (ClassJournalEditWindow.CanCorrect) : lui seul connaît l'heure serveur et, pour
         * l'Enseignant, sa propre fiche enseignant. Ce getter n'est qu'un nom lisible dans la vue —
         * la vraie garde reste le 403 de Update/Delete si ce champ se trompait.
         */
        canEditEntry(entry) {
            return entry.canEdit;
        },

        async init() {
            await this.loadReferenceData();
            await this.load();
        },

        async loadReferenceData() {
            try {
                const requests = [window.api.get('/classrooms'), window.api.get('/subjects')];
                const [classrooms, subjects] = await Promise.all(requests);
                this.classrooms = classrooms || [];
                this.subjects = subjects || [];
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des données de référence.');
            }
        },

        // ================================================================== LISTE
        entries: [],
        totalCount: 0,
        page: 1,
        pageSize: 20,
        isLoading: false,
        filters: { classroomId: '', subjectId: '', periodStart: '', periodEnd: '' },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                const params = new URLSearchParams({ page: this.page, pageSize: this.pageSize });
                if (this.filters.classroomId) params.set('classroomId', this.filters.classroomId);
                if (this.filters.subjectId) params.set('subjectId', this.filters.subjectId);
                if (this.filters.periodStart) params.set('periodStart', this.filters.periodStart);
                if (this.filters.periodEnd) params.set('periodEnd', this.filters.periodEnd);

                const data = await window.api.get(`/class-journal?${params.toString()}`);
                this.entries = (data && data.items) || [];
                this.totalCount = (data && data.totalCount) || 0;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du cahier de texte.');
            } finally {
                this.isLoading = false;
            }
        },

        applyFilters() {
            this.page = 1;
            this.load();
        },

        resetFilters() {
            this.filters = { classroomId: '', subjectId: '', periodStart: '', periodEnd: '' };
            this.applyFilters();
        },

        // ================================================================== DÉTAIL
        detailEntry: null,

        openDetail(entry) { this.detailEntry = entry; },
        closeDetail() { this.detailEntry = null; },

        // ================================================================== CRÉATION
        isCreateOpen: false,
        isSaving: false,
        newEntry: { classroomId: '', subjectId: '', sessionDate: '', topic: '', content: '', homework: '', homeworkDueDate: '' },
        createErrors: {},

        openCreate() {
            this.newEntry = { classroomId: '', subjectId: '', sessionDate: '', topic: '', content: '', homework: '', homeworkDueDate: '' };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        closeCreate() { this.isCreateOpen = false; },

        async submitCreate() {
            this.isSaving = true;
            this.createErrors = {};
            try {
                await window.api.post('/class-journal', {
                    classroomId: this.newEntry.classroomId,
                    subjectId: this.newEntry.subjectId,
                    sessionDate: this.newEntry.sessionDate,
                    topic: this.newEntry.topic,
                    content: this.newEntry.content,
                    homework: this.newEntry.homework || null,
                    homeworkDueDate: this.newEntry.homework ? (this.newEntry.homeworkDueDate || null) : null
                });
                this.isCreateOpen = false;
                toast.success('Séance journalisée.');
                await this.load();
            } catch (err) {
                if (err.code === 'SCHEDULE_SLOT_NOT_PLANNED') {
                    this.createErrors = { global: err.message };
                } else {
                    this.createErrors = window.api.toFieldErrors(err, 'Erreur lors de la journalisation de la séance.');

                    // toFieldErrors ne pose PAS de clé `global` quand il a réussi à ventiler les
                    // erreurs par champ (400/422) — or Classe/Matière/Date sont EN HAUT du formulaire,
                    // au-dessus de la ligne de flottaison une fois que l'utilisateur a défilé jusqu'à
                    // Sujet/Contenu/Devoirs : sans résumé ni défilement, « Enregistrer » ne produisait
                    // aucun retour visible (même bug que Students.submitCreate, même correctif).
                    if (!this.createErrors.global) {
                        this.createErrors.global = Object.keys(this.createErrors).length > 0
                            ? 'Certains champs doivent être corrigés — voir les indications en rouge ci-dessous.'
                            : 'Erreur lors de la journalisation de la séance.';
                    }
                }
                this.$nextTick(() => {
                    this.$refs.createEntryError?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                });
            } finally {
                this.isSaving = false;
            }
        },

        // ================================================================== ÉDITION
        editingEntry: null, // { id, topic, content, homework, homeworkDueDate, rowVersion }
        isSavingEdit: false,
        editErrors: {},

        openEdit(entry) {
            this.editingEntry = {
                id: entry.id, topic: entry.topic, content: entry.content,
                homework: entry.homework || '', homeworkDueDate: entry.homeworkDueDate || '',
                rowVersion: entry.rowVersion
            };
            this.editErrors = {};
        },

        closeEdit() { this.editingEntry = null; },

        async submitEdit() {
            if (!this.editingEntry) return;

            this.isSavingEdit = true;
            this.editErrors = {};
            try {
                await window.api.put(`/class-journal/${this.editingEntry.id}`, {
                    topic: this.editingEntry.topic,
                    content: this.editingEntry.content,
                    homework: this.editingEntry.homework || null,
                    homeworkDueDate: this.editingEntry.homework ? (this.editingEntry.homeworkDueDate || null) : null,
                    rowVersion: this.editingEntry.rowVersion
                });
                this.closeEdit();
                toast.success('Entrée corrigée.');
                await this.load();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.editErrors = { global: "Cette entrée vient d'être modifiée par un autre utilisateur. La liste a été rafraîchie — vérifiez les valeurs puis réessayez." };
                    await this.load();
                } else {
                    this.editErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification.');

                    if (!this.editErrors.global) {
                        this.editErrors.global = Object.keys(this.editErrors).length > 0
                            ? 'Certains champs doivent être corrigés — voir les indications en rouge ci-dessous.'
                            : 'Erreur lors de la modification.';
                    }
                }
                this.$nextTick(() => {
                    this.$refs.editEntryError?.scrollIntoView({ behavior: 'smooth', block: 'center' });
                });
            } finally {
                this.isSavingEdit = false;
            }
        },

        // ================================================================== SUPPRESSION
        deletingEntry: null, // { id, topic, rowVersion }
        isDeleting: false,
        deleteError: null,

        openDelete(entry) {
            this.deletingEntry = { id: entry.id, topic: entry.topic, rowVersion: entry.rowVersion };
            this.deleteError = null;
        },

        closeDelete() {
            this.deletingEntry = null;
            this.deleteError = null;
        },

        async confirmDelete() {
            if (!this.deletingEntry) return;

            this.isDeleting = true;
            this.deleteError = null;
            try {
                await window.api.delete(`/class-journal/${this.deletingEntry.id}?rowVersion=${this.deletingEntry.rowVersion}`);
                this.deletingEntry = null;
                toast.success('Entrée supprimée.');
                await this.load();
            } catch (err) {
                if (err.code === 'CONCURRENCY_CONFLICT') {
                    this.deleteError = "Cette entrée vient d'être modifiée par un autre utilisateur. Rafraîchissez la page puis réessayez.";
                    await this.load();
                } else {
                    this.deleteError = (err && err.message) || 'Erreur lors de la suppression.';
                }
            } finally {
                this.isDeleting = false;
            }
        }
    }));
});
