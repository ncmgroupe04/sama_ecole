/**
 * JGK-D06 — Présences (écran d'appel). On choisit une classe, une matière, une date et un créneau,
 * on charge le roster (GET /attendance/roster), on coche les statuts, puis on enregistre l'appel
 * complet (POST /attendance).
 *
 * Portée : l'Enseignant ne peut faire l'appel que pour ses classes/matières assignées — la garde est
 * côté API (403 « FORBIDDEN » si non assigné, ou compte non rattaché à une fiche enseignant). Ce
 * n'est pas une panne : window.api la présente dans la modale universelle « Accès refusé »
 * (access-denied.js) et neutralise le message local (toMessage renvoie ''), donc rien à traiter ici.
 * Directeur et Secrétariat ne sont pas bornés.
 */
document.addEventListener('alpine:init', () => {
    // Complément N°5 bis : l'enseignant ne pointe que la présence ou l'absence sur le créneau. Un RETARD n'a plus
    // d'autre source qu'un billet d'entrée (Surveillance › Billets d'entrée) — le serveur refuse tout « Retard »
    // sans billet (422). Les lignes issues d'un billet, et les retards historiques, s'affichent donc en lecture seule.
    const STATUS_OPTIONS = [
        { value: 'Present', label: 'Présent' },
        { value: 'JustifiedAbsence', label: 'Absent (justifié)' },
        { value: 'UnjustifiedAbsence', label: 'Absent (non justifié)' }
    ];

    Alpine.data('attendanceView', () => ({
        classrooms: [],
        subjects: [],
        statusOptions: STATUS_OPTIONS,

        // Filtres de l'appel.
        filters: { classroomId: '', subjectId: '', date: '', period: 'Matin' },

        // Appel par CRÉNEAU d'emploi du temps (Évolution N°5). `mode` est le choix de l'utilisateur ; l'appel n'est
        // réellement « par créneau » (`slotMode`) que si la classe a des cours ce jour-là — sinon, ou sur choix,
        // c'est l'appel LIBRE (demi-journée, période saisie) d'avant. Confort d'affichage : le serveur revérifie
        // tout (cours de la classe et de la matière, jour, titulaire, jour de repos).
        mode: 'slot', // 'slot' | 'free'
        slots: [],
        selectedSlotId: '',
        ticketNotice: null,

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
            // Jours ouvrés de l'établissement (bandeau « jour de repos ») : store partagé, un seul fetch.
            Alpine.store('schoolConfig').init();
            this.filters.date = this.toIsoDate(new Date());
            this.loadClassrooms();
            this.loadSubjects();

            // Les cours du jour dépendent de la classe et de la date — et de la config d'établissement (jour de
            // repos), qui arrive de façon asynchrone. $watch n'existe pas hors d'Alpine (tests) : les méthodes
            // restent appelables directement.
            if (typeof this.$watch === 'function') {
                this.$watch('filters.classroomId', () => this.onSlotScopeChanged());
                this.$watch('filters.date', () => this.onSlotScopeChanged());
            }
            Promise.resolve(Alpine.store('schoolConfig').init()).then(() => this.onSlotScopeChanged());
        },

        /** Le créneau à afficher : celui du cours choisi, ou la période saisie en appel libre. */
        get periodLabel() {
            if (!this.slotMode) return this.filters.period;
            const slot = this.slots.find((s) => s.slotId === this.selectedSlotId);
            return slot ? slot.label : '';
        },

        /** Vrai quand l'appel se fait sur un cours de l'emploi du temps (et non en mode libre). */
        get slotMode() {
            return this.mode === 'slot' && this.slots.length > 0;
        },

        setMode(mode) {
            this.mode = mode;
            this.selectedSlotId = '';
            this.rosterLoaded = false;
            this.ticketNotice = null;
        },

        onSlotScopeChanged() {
            this.rosterLoaded = false;
            this.ticketNotice = null;
            return this.loadSlots();
        },

        /** Les cours de la classe pour la date choisie — vides un jour de repos, où l'appel est de toute façon refusé. */
        async loadSlots() {
            this.slots = [];
            this.selectedSlotId = '';
            if (!this.filters.classroomId || !this.filters.date || this.isRestDay) return;

            try {
                const params = new URLSearchParams({ classroomId: this.filters.classroomId, date: this.filters.date });
                const data = await window.api.get(`/attendance/slots?${params.toString()}`);
                this.slots = Array.isArray(data) ? data : [];
            } catch (err) {
                // Non bloquant : sans liste de cours, l'appel libre reste possible — mais l'utilisateur est prévenu,
                // une liste vide ne devant jamais se confondre avec « aucun cours » (garde-fou des chargements muets).
                console.error('Erreur chargement des cours du jour:', err);
                this.slots = [];
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des cours du jour.'));
            }
        },

        /** Choisir un cours fixe la matière (celle du cours) et ouvre directement sa feuille d'appel. */
        async selectSlot(slot) {
            this.selectedSlotId = slot.slotId;
            this.filters.subjectId = slot.subjectId;
            await this.loadRoster();
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
                this.classrooms = [];
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des classes.'));
            }
        },

        async loadSubjects() {
            try {
                const data = await window.api.get('/subjects');
                this.subjects = Array.isArray(data) ? data : (data.items || []);
            } catch (err) {
                console.error('Erreur chargement matières:', err);
                this.subjects = [];
                toast.error(window.api.toMessage(err, 'Erreur lors du chargement des matières.'));
            }
        },

        /** Vrai quand la date choisie est un jour de repos de l'établissement (confort : le serveur refuse en 422). */
        get isRestDay() {
            return Boolean(this.filters.date) && !Alpine.store('schoolConfig').isWorkingDay(this.filters.date);
        },

        get canLoad() {
            if (this.isRestDay || !this.filters.classroomId || !this.filters.date) return false;

            // Par créneau : il suffit d'avoir choisi un cours (la matière en découle, la période est dérivée).
            if (this.slotMode) return !!this.selectedSlotId;

            return !!(this.filters.subjectId && this.filters.period.trim());
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
                    date: this.filters.date
                });
                // Par créneau : le serveur dérive la période du cours — on n'en envoie aucune.
                if (this.slotMode) params.set('scheduleSlotId', this.selectedSlotId);
                else params.set('period', this.filters.period.trim());
                const roster = await window.api.get(`/attendance/roster?${params.toString()}`);

                // Statut par défaut « Présent » pour une grille vierge ; sinon on reprend l'appel déjà saisi.
                this.entries = (roster.students || []).map((s) => ({
                    studentId: s.studentId,
                    matricule: s.matricule,
                    fullName: s.fullName,
                    status: s.status || 'Present',
                    lateMinutes: s.lateMinutes || 0,
                    // Verrouillée : la ligne vient d'un billet d'entrée, ou c'est un retard historique — aucune option
                    // de la liste ne la porte plus, elle repart telle quelle.
                    locked: !!s.entryTicketId || s.status === 'Late',
                    // Billet d'entrée visant ce cours (Évolution N°5) : présélectionne le retard côté serveur.
                    entryTicketId: s.entryTicketId ?? null,
                    entryTicketNumber: s.entryTicketNumber ?? null,
                    entryTicketStatus: s.entryTicketStatus ?? null
                }));
                this.alreadySubmitted = roster.alreadySubmitted;
                this.rosterLoaded = true;
            } catch (err) {
                // 403 « FORBIDDEN » (non assigné, compte non rattaché) → modale universelle « Accès
                // refusé » via window.api, toMessage renvoie alors '' et le bandeau reste muet.
                // 422 (classe/matière/année invalide)… → bandeau d'erreur classique.
                this.error = window.api.toMessage(err, "Erreur lors du chargement de l'appel.");
            } finally {
                this.isLoading = false;
            }
        },

        /** Compteur d'en-tête : combien de lignes ont ce statut dans la saisie courante. */
        countBy(status) {
            return this.entries.filter((e) => e.status === status).length;
        },

        /** Présents = présents + arrivés en retard (billet) : même règle que le taux de présence. */
        get presentCount() {
            return this.countBy('Present') + this.countBy('Late');
        },

        /** Texte de la pastille d'une ligne verrouillée : le statut ET son origine. */
        rowStatusLabel(entry) {
            const fromTicket = entry.entryTicketId ? ' — billet' : '';
            if (entry.status === 'Late') return `Retard (${Number(entry.lateMinutes) || 0} min)${fromTicket}`;
            const option = STATUS_OPTIONS.find((o) => o.value === entry.status);
            return `${option ? option.label : entry.status}${fromTicket}`;
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
                const payload = {
                    classroomId: this.filters.classroomId,
                    subjectId: this.filters.subjectId,
                    date: this.filters.date,
                    entries: this.entries.map((e) => ({
                        studentId: e.studentId,
                        status: e.status,
                        lateMinutes: e.status === 'Late' ? Number(e.lateMinutes) || 0 : 0
                    }))
                };
                // Par créneau : le serveur dérive la période du cours ; en mode libre, c'est celle qui est saisie.
                if (this.slotMode) payload.scheduleSlotId = this.selectedSlotId;
                else payload.period = this.filters.period.trim();

                await window.api.postWithRetry('/attendance', payload, {
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
                    // 409 dès le premier essai : appel déjà enregistré pour ce créneau ; 422 : saisie
                    // invalide. 403 « FORBIDDEN » (non assigné) → modale universelle via window.api,
                    // toMessage renvoie alors '' et le bandeau reste muet.
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

        // ------------------------------------------------------------ Billets d'entrée (Évolution N°5)

        ticketStatusLabel(status) {
            switch (status) {
                case 'Issued': return 'en attente d\'acceptation';
                case 'Accepted': return 'accepté';
                case 'Cancelled': return 'annulé';
                default: return '';
            }
        },

        /**
         * « Accepter » : l'enseignant du cours (le serveur ne lui montre que SES cours) ou le Directeur, tant que le
         * billet est en attente. Confort d'affichage — le serveur refuse (403) tout autre compte.
         */
        canAcceptTicket(entry) {
            return !!entry.entryTicketId
                && entry.entryTicketStatus === 'Issued'
                && (window.auth.role === 'Enseignant' || window.auth.role === 'Directeur');
        },

        async acceptTicket(entry) {
            if (!this.canAcceptTicket(entry)) return;

            this.ticketNotice = null;
            this.error = null;
            try {
                await window.api.post(`/billets/${entry.entryTicketId}/accept`, {});
                // Le serveur a pu repasser la ligne à « Retard » : on relit la feuille plutôt que de deviner.
                await this.loadRoster();
                this.ticketNotice = `Billet ${entry.entryTicketNumber || ''} accepté : ${entry.fullName} est admis(e) en classe.`.replace('  ', ' ');
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'acceptation du billet.");
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
