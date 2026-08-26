/**
 * JGK-D06 — Présences (écran d'appel). On choisit une classe, une matière, une date et un créneau,
 * on charge le roster (GET /attendance/roster), on coche les statuts, puis on enregistre l'appel
 * complet (POST /attendance).
 *
 * Portée : l'Enseignant ne peut faire l'appel que pour ses classes/matières assignées — la garde est
 * côté API (403 si non assigné), on se contente ici d'afficher le message renvoyé. Directeur et
 * Secrétariat ne sont pas bornés.
 */
document.addEventListener('alpine:init', () => {
    const STATUS_OPTIONS = [
        { value: 'Present', label: 'Présent' },
        { value: 'Late', label: 'Retard' },
        { value: 'JustifiedAbsence', label: 'Absent (justifié)' },
        { value: 'UnjustifiedAbsence', label: 'Absent (non justifié)' }
    ];

    Alpine.data('attendanceView', () => ({
        classrooms: [],
        subjects: [],
        statusOptions: STATUS_OPTIONS,

        // Filtres de l'appel.
        filters: { classroomId: '', subjectId: '', date: '', period: 'Matin' },

        // Roster chargé + saisie.
        entries: [],
        rosterLoaded: false,
        alreadySubmitted: false,
        isLoading: false,
        error: null,

        submitting: false,
        submitError: null,
        submitSuccess: false,

        init() {
            this.filters.date = this.toIsoDate(new Date());
            this.loadClassrooms();
            this.loadSubjects();
        },

        toIsoDate(d) {
            const month = String(d.getMonth() + 1).padStart(2, '0');
            const day = String(d.getDate()).padStart(2, '0');
            return `${d.getFullYear()}-${month}-${day}`;
        },

        /** Même patron que school-years.js : jj/mm/aaaa, la date de l'appel est déjà au format ISO local. */
        formatDate(isoDate) {
            if (!isoDate) return '';
            const [year, month, day] = isoDate.split('-');
            return `${day}/${month}/${year}`;
        },

        get selectedClassroomLabel() {
            const c = this.classrooms.find((x) => x.id === this.filters.classroomId);
            return c ? `${c.name} (${c.level})` : '';
        },

        get selectedSubjectLabel() {
            const s = this.subjects.find((x) => x.id === this.filters.subjectId);
            return s ? s.name : '';
        },

        async loadClassrooms() {
            try {
                const data = await window.api.get('/classrooms');
                this.classrooms = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement classes:', err);
            }
        },

        async loadSubjects() {
            try {
                const data = await window.api.get('/subjects');
                this.subjects = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement matières:', err);
            }
        },

        get canLoad() {
            return this.filters.classroomId && this.filters.subjectId && this.filters.date && this.filters.period.trim();
        },

        async loadRoster() {
            if (!this.canLoad) return;

            this.isLoading = true;
            this.error = null;
            this.submitError = null;
            this.submitSuccess = false;
            this.rosterLoaded = false;
            try {
                const params = new URLSearchParams({
                    classroomId: this.filters.classroomId,
                    subjectId: this.filters.subjectId,
                    date: this.filters.date,
                    period: this.filters.period.trim()
                });
                const roster = await window.api.get(`/attendance/roster?${params.toString()}`);

                // Statut par défaut « Présent » pour une grille vierge ; sinon on reprend l'appel déjà saisi.
                this.entries = (roster.students || []).map((s) => ({
                    studentId: s.studentId,
                    matricule: s.matricule,
                    fullName: s.fullName,
                    status: s.status || 'Present',
                    lateMinutes: s.lateMinutes || 0
                }));
                this.alreadySubmitted = roster.alreadySubmitted;
                this.rosterLoaded = true;
            } catch (err) {
                // 403 (enseignant non assigné), 422 (classe/matière/année invalide)… : on affiche le message serveur.
                this.error = window.api.toMessage(err, "Erreur lors du chargement de l'appel.");
            } finally {
                this.isLoading = false;
            }
        },

        onStatusChange(entry) {
            // Les minutes de retard n'ont de sens que pour « Retard » — on les remet à zéro sinon
            // (même invariant que côté serveur).
            if (entry.status !== 'Late') entry.lateMinutes = 0;
        },

        /** Compteur d'en-tête : combien de présents/retards/absents dans la saisie courante. */
        countBy(status) {
            return this.entries.filter((e) => e.status === status).length;
        },

        // 'sending' | 'retrying' | 'done' | 'failed' — piloté par onStateChange (ticket JGK-L03).
        sendState: null,

        async submit() {
            this.submitting = true;
            this.submitError = null;
            this.submitSuccess = false;
            this.sendState = null;

            // Dernier numéro de tentative rapporté par submitWithRetry (JGK-L02) : distingue un 409
            // reçu DÈS LE PREMIER essai (vrai conflit — fiche déjà saisie par quelqu'un d'autre) d'un
            // 409 reçu APRÈS un retry (la tentative précédente a bien été enregistrée côté serveur,
            // seule sa réponse s'est perdue en route — voir le catch ci-dessous).
            let lastAttempt = 1;

            try {
                // submitWithRetry ne rejoue QUE sur une coupure réseau, jamais sur une réponse HTTP —
                // SubmitAttendanceSheetCommand est déjà idempotent par construction (contrainte
                // d'unicité classe/matière/date/créneau, AGENTS.md JGK-L01), inutile d'y ajouter une clé.
                await window.api.postWithRetry('/attendance', {
                    classroomId: this.filters.classroomId,
                    subjectId: this.filters.subjectId,
                    date: this.filters.date,
                    period: this.filters.period.trim(),
                    entries: this.entries.map((e) => ({
                        studentId: e.studentId,
                        status: e.status,
                        lateMinutes: e.status === 'Late' ? Number(e.lateMinutes) || 0 : 0
                    }))
                }, {
                    onStateChange: (state, attempt) => {
                        this.sendState = state;
                        lastAttempt = attempt;
                    }
                });
                this.submitSuccess = true;
                this.alreadySubmitted = true;
            } catch (err) {
                if (err.status === 409 && lastAttempt > 1) {
                    // Succès silencieux (critère JGK-L03) : pas de message d'erreur trompeur alors que
                    // l'appel a bel et bien été enregistré.
                    this.submitSuccess = true;
                    this.alreadySubmitted = true;
                } else {
                    // 409 dès le premier essai : appel déjà enregistré pour ce créneau ; 403 : non
                    // assigné ; 422 : saisie invalide.
                    this.submitError = window.api.toMessage(err, "Erreur lors de l'enregistrement de l'appel.");
                }
            } finally {
                this.submitting = false;
            }
        },

        /** Libellé de l'état d'envoi affiché près du bouton, piloté par submitWithRetry (JGK-L03). */
        sendStateLabel() {
            switch (this.sendState) {
                case 'retrying': return 'Connexion instable — nouvelle tentative en cours, en attente d\'envoi…';
                case 'failed': return 'Échec après plusieurs tentatives — la saisie est conservée, réessayez.';
                default: return '';
            }
        },

        statusBadgeClass(status) {
            switch (status) {
                case 'Present': return 'bg-success-bg text-success';
                case 'Late': return 'bg-warning-bg text-warning';
                case 'JustifiedAbsence': return 'bg-primary-50 text-primary';
                default: return 'bg-danger-bg text-danger';
            }
        },

        initials(name) {
            return (name || '').split(' ').filter(Boolean).slice(0, 2).map((p) => p[0]).join('').toUpperCase();
        }
    }));
});
