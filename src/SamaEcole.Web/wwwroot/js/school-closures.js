/**
 * Calendrier de fermeture (jours fériés, vacances) — Franco-Arabe Bloc 5a. Écran : onglet « Années
 * scolaires » de Paramètres (partial _SchoolClosuresPanel).
 *
 * Le serveur reste seul juge (SchoolCalendarController : lecture ouverte aux rôles du cahier,
 * écriture Directeur, période tenue dans l'année, 422 sinon). Les contrôles faits ici ne servent
 * qu'à éviter un aller-retour inutile sur une connexion mobile lente.
 */
(function () {
    const KIND_LABELS = { Holiday: 'Jour férié', Vacation: 'Vacances', Other: 'Autre fermeture' };

    /** « 2027-06-06 » → « 06/06/2027 », sans passer par Date (aucun décalage de fuseau possible). */
    function formatDate(iso) {
        if (!iso || !/^\d{4}-\d{2}-\d{2}$/.test(iso)) return iso || '';
        const [y, m, d] = iso.split('-');
        return `${d}/${m}/${y}`;
    }

    /** Un seul jour → « 06/06/2027 » ; sinon « 19/12/2026 → 03/01/2027 ». */
    function formatRange(start, end) {
        return !end || start === end ? formatDate(start) : `${formatDate(start)} → ${formatDate(end)}`;
    }

    /** Nombre de jours calendaires couverts (bornes incluses). Dates ISO comparées en UTC. */
    function dayCount(start, end) {
        const ms = Date.parse(`${end}T00:00:00Z`) - Date.parse(`${start}T00:00:00Z`);
        return Number.isFinite(ms) ? Math.round(ms / 86400000) + 1 : 0;
    }

    /**
     * Contrôle de saisie. Renvoie un objet d'erreurs indexé par champ (clés en minuscules, comme
     * window.api.toFieldErrors) ; vide = valide. `year` porte {startDate, endDate, label}.
     */
    function validate(form, year) {
        const errors = {};
        if (!form.label || !form.label.trim()) errors.label = 'Le libellé est obligatoire.';
        else if (form.label.length > 120) errors.label = 'Le libellé ne peut pas dépasser 120 caractères.';
        if (form.labelAr && form.labelAr.length > 120) errors.labelar = 'Le libellé arabe ne peut pas dépasser 120 caractères.';
        if (!form.startDate) errors.startdate = 'La date de début est obligatoire.';
        if (!form.endDate) errors.enddate = 'La date de fin est obligatoire.';
        if (form.startDate && form.endDate && form.endDate < form.startDate) {
            errors.enddate = 'La date de fin ne peut pas précéder la date de début.';
        }
        if (year && form.startDate && form.endDate && !errors.enddate
            && (form.startDate < year.startDate || form.endDate > year.endDate)) {
            errors.startdate = `La fermeture doit tenir dans l'année « ${year.label} » `
                + `(${formatDate(year.startDate)} – ${formatDate(year.endDate)}).`;
        }
        return errors;
    }

    window.schoolClosuresLogic = { KIND_LABELS, formatDate, formatRange, dayCount, validate };

    document.addEventListener('alpine:init', () => {
        Alpine.data('schoolClosuresView', () => ({
            years: [],
            yearId: '',
            closures: [],
            isLoading: false,
            error: null,

            /** Confort d'affichage : le contrôleur est [Authorize(Roles = Directeur)] pour écrire. */
            isDirecteur: window.auth.role === 'Directeur',

            // Formulaire (création et modification partagent le même).
            isFormOpen: false,
            isSaving: false,
            editingId: null,
            form: { kind: 'Holiday', startDate: '', endDate: '', label: '', labelAr: '' },
            formErrors: {},

            // Suppression en deux temps, dans la ligne (pas de modale pour un geste aussi léger).
            pendingDeleteId: null,

            kindLabels: KIND_LABELS,
            formatRange,
            dayCount,

            get year() {
                return this.years.find((y) => y.id === this.yearId) || null;
            },

            async init() {
                await this.loadYears();
            },

            async loadYears() {
                this.isLoading = true;
                this.error = null;
                try {
                    this.years = await window.api.get('/school-years');
                    const active = this.years.find((y) => y.isActive) || this.years[0];
                    this.yearId = active ? active.id : '';
                    await this.loadClosures();
                } catch (err) {
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement du calendrier.');
                } finally {
                    this.isLoading = false;
                }
            },

            async loadClosures() {
                this.closeForm();
                this.pendingDeleteId = null;
                if (!this.yearId) {
                    this.closures = [];
                    return;
                }
                try {
                    this.error = null;
                    this.closures = await window.api.get(`/school-calendar/closures?schoolYearId=${this.yearId}`);
                } catch (err) {
                    this.closures = [];
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement des fermetures.');
                }
            },

            openCreate() {
                this.editingId = null;
                this.form = { kind: 'Holiday', startDate: '', endDate: '', label: '', labelAr: '' };
                this.formErrors = {};
                this.isFormOpen = true;
            },

            openEdit(closure) {
                // Copie de TRAVAIL, jamais la ligne du tableau (même précaution que school-years.js).
                this.editingId = closure.id;
                this.form = {
                    kind: closure.kind,
                    startDate: closure.startDate,
                    endDate: closure.endDate,
                    label: closure.label,
                    labelAr: closure.labelAr || ''
                };
                this.formErrors = {};
                this.isFormOpen = true;
            },

            closeForm() {
                this.isFormOpen = false;
                this.editingId = null;
                this.formErrors = {};
            },

            /** Saisir un seul jour : la date de fin suit la date de début tant qu'elle est vide ou antérieure. */
            onStartChanged() {
                if (!this.form.endDate || this.form.endDate < this.form.startDate) {
                    this.form.endDate = this.form.startDate;
                }
            },

            async submit() {
                this.formErrors = validate(this.form, this.year);
                if (Object.keys(this.formErrors).length > 0) return;

                const body = {
                    kind: this.form.kind,
                    startDate: this.form.startDate,
                    endDate: this.form.endDate,
                    label: this.form.label.trim(),
                    labelAr: this.form.labelAr && this.form.labelAr.trim() ? this.form.labelAr.trim() : null
                };

                this.isSaving = true;
                try {
                    if (this.editingId) {
                        await window.api.put(`/school-calendar/closures/${this.editingId}`, body);
                    } else {
                        await window.api.post(`/school-calendar/closures?schoolYearId=${this.yearId}`, body);
                    }
                    await this.loadClosures();
                } catch (err) {
                    this.formErrors = window.api.toFieldErrors(err, "Erreur lors de l'enregistrement de la fermeture.");
                } finally {
                    this.isSaving = false;
                }
            },

            askDelete(closure) {
                this.pendingDeleteId = closure.id;
            },

            cancelDelete() {
                this.pendingDeleteId = null;
            },

            async confirmDelete(closure) {
                try {
                    await window.api.delete(`/school-calendar/closures/${closure.id}`);
                    await this.loadClosures();
                } catch (err) {
                    this.pendingDeleteId = null;
                    this.error = window.api.toMessage(err, 'Erreur lors de la suppression de la fermeture.');
                }
            }
        }));
    });
})();
