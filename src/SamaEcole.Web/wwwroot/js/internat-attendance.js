/**
 * Onglet « Pointage » de l'écran Internat (/internat) — feuille d'appel du soir d'un pavillon et pointage collectif, sur /api/v1/boarding (lot G4, tâche 11).
 *
 * Données : GET /boarding/attendance?dormitoryId=&date= (les internes du pavillon, triés chambre → lit, permissions pré-remplies « absent ») ;
 * POST /boarding/attendance {date, dormitoryId, entries[]} (réponse LIGNE PAR LIGNE : une ligne refusée n'empêche pas les autres) ;
 * registre imprimable : GET /boarding/dormitories/{id}/register-pdf?date= par la modale d'aperçu commune.
 *
 * Règles reflétées (le serveur reste le juge) :
 *   - un pensionnaire en permission ne peut être pointé qu'absent (sinon BOARDER_ON_LEAVE) : la case « présent » lui est verrouillée ;
 *   - la nuit n'est jamais dans le futur (refusée avant l'appel) ; hors année scolaire active, le serveur répond 422 et son message est affiché ;
 *   - seules les lignes DÉCIDÉES et MODIFIÉES sont envoyées : une ligne déjà enregistrée n'est pas renvoyée (l'envoi est un upsert, mais inutile) ;
 *   - un résultat mixte garde les rejets visibles (message par ligne) à côté des lignes enregistrées ;
 *   - changer de pavillon ou de nuit avec une saisie non enregistrée demande confirmation : sur 200 lignes, la perdre en silence serait grave.
 *
 * La logique pure vit dans `window.attendanceLogic`, testée sans navigateur (tests/js/internat-attendance.test.mjs).
 */
window.attendanceLogic = (() => {
    const MAX_NOTE = 300;
    /** Même note que le serveur pose à un absent en permission quand l'opérateur n'en saisit pas (AttendanceRules.OnLeaveNote). */
    const ON_LEAVE_NOTE = 'En permission';

    const REJECTION_MESSAGES = {
        NOT_IN_DORMITORY: "Ce pensionnaire n'a pas (ou plus) de lit dans ce pavillon : rechargez la feuille d'appel.",
        DUPLICATE_ENTRY: 'Ce pensionnaire figurait deux fois dans la saisie : la première ligne a été retenue.',
        BOARDER_ON_LEAVE: 'Ce pensionnaire est en sortie cette nuit-là : il ne peut être pointé qu\'absent.'
    };

    function blankToNull(value) {
        const text = typeof value === 'string' ? value.trim() : '';
        return text === '' ? null : text;
    }

    /** Lignes de travail d'une feuille : la valeur enregistrée (`base`) sert à savoir ce qui a changé. */
    function toLines(sheet) {
        return sheet.lines.map((l) => ({
            boarderId: l.boarderId,
            studentName: l.studentName,
            matricule: l.matricule,
            roomName: l.roomName,
            bedNumber: l.bedNumber,
            onLeave: !!l.onLeave,
            recorded: !!l.recorded,
            isPresent: l.isPresent === undefined ? null : l.isPresent,
            note: l.note ?? null,
            base: { recorded: !!l.recorded, isPresent: l.isPresent === undefined ? null : l.isPresent, note: l.note ?? null },
            rejection: ''
        }));
    }

    /** Pointe une ligne. Un pensionnaire en permission ne peut pas être « présent » : refus (faux), la ligne reste inchangée. */
    function setPresence(line, present) {
        if (line.onLeave && present) return false;
        line.isPresent = present;
        return true;
    }

    /** Vrai si la ligne doit partir : décidée, et jamais enregistrée ou différente de ce qui est enregistré. */
    function isDirty(line) {
        if (line.isPresent === null || line.isPresent === undefined) return false;
        if (!line.base.recorded) return true;
        return line.isPresent !== line.base.isPresent || blankToNull(line.note) !== blankToNull(line.base.note);
    }

    /**
     * Vrai si l'UTILISATEUR a changé quelque chose depuis le chargement (ou depuis le dernier enregistrement). Différent de isDirty : une permission
     * pré-remplie par le serveur est « à envoyer » dès l'ouverture, sans que personne n'ait rien saisi — elle ne doit pas déclencher la demande d'abandon.
     */
    function hasUnsavedInput(lines) {
        return lines.some((l) => l.isPresent !== l.base.isPresent || blankToNull(l.note) !== blankToNull(l.base.note));
    }

    /** Corps minimal de POST : seules les lignes à envoyer. */
    function dirtyEntries(lines) {
        return lines.filter(isDirty).map((l) => ({ boarderId: l.boarderId, isPresent: l.isPresent, note: blankToNull(l.note) }));
    }

    /** « Tout présent » : ne touche ni les décisions déjà prises ni les pensionnaires en permission. */
    function markAllPresent(lines) {
        lines.forEach((l) => {
            if (!l.onLeave && (l.isPresent === null || l.isPresent === undefined)) l.isPresent = true;
        });
    }

    function validateDate(date, today) {
        if (!date) return 'Indiquez la date de la nuit à pointer.';
        if (date > today) return 'Le pointage ne peut pas porter sur une date future.';
        return '';
    }

    function validateNotes(lines) {
        return lines.some((l) => typeof l.note === 'string' && l.note.length > MAX_NOTE) ? `Une note ne doit pas dépasser ${MAX_NOTE} caractères.` : '';
    }

    function rejectionMessage(code, serverMessage) {
        return REJECTION_MESSAGES[code] || serverMessage || 'Ligne refusée par le serveur.';
    }

    /** Reporte le résultat du serveur sur les lignes : succès = enregistrée et propre, rejet = message conservé et ligne toujours à traiter. */
    function applyResult(lines, result) {
        result.lines.forEach((r) => {
            const line = lines.find((l) => l.boarderId === r.boarderId);
            if (!line) return;
            if (r.outcome === 'Saved') {
                // Le serveur pose « En permission » à un absent en sortie sans note : on l'aligne pour que la ligne ne paraisse pas modifiée.
                if (line.isPresent === false && line.onLeave && blankToNull(line.note) === null) line.note = ON_LEAVE_NOTE;
                line.recorded = true;
                line.base = { recorded: true, isPresent: line.isPresent, note: line.note };
                line.rejection = '';
            } else {
                line.rejection = rejectionMessage(r.code, r.message);
            }
        });
    }

    function counts(lines) {
        return {
            present: lines.filter((l) => l.isPresent === true).length,
            absent: lines.filter((l) => l.isPresent === false).length,
            pending: lines.filter((l) => l.isPresent === null || l.isPresent === undefined).length,
            dirty: lines.filter(isDirty).length
        };
    }

    return {
        MAX_NOTE, ON_LEAVE_NOTE, toLines, setPresence, isDirty, hasUnsavedInput, dirtyEntries, markAllPresent, validateDate, validateNotes, rejectionMessage, applyResult, counts
    };
})();

document.addEventListener('alpine:init', () => {
    Alpine.data('attendanceTab', () => ({
        // Modale d'aperçu PDF commune (wwwroot/js/pdf-preview.js) : registre du pavillon.
        ...window.pdfPreview.state(),

        loading: false,
        saving: false,
        error: '',
        dateError: '',
        notice: '',
        dormitories: [],
        // Sélection en cours (liée aux champs) et feuille réellement affichée : elles diffèrent le temps d'une confirmation d'abandon.
        sel: { dormitoryId: '', date: '' },
        loaded: null,
        sheetName: '',
        lines: [],
        result: null,
        discard: { open: false },
        sheetSeq: 0,

        async init() {
            this.sel.date = window.internatHelpers.today();
            await this.loadDormitories();
            // Un seul pavillon : inutile de le faire choisir.
            if (this.dormitories.length === 1) {
                this.sel.dormitoryId = this.dormitories[0].id;
                await this.onSelectionChange();
            }
        },

        helpers() { return window.internatHelpers; },
        logic() { return window.attendanceLogic; },

        get dormitoryOptions() { return this.dormitories.map((d) => ({ value: d.id, label: d.name })); },
        get hasSheet() { return this.loaded !== null; },
        get counts() { return this.logic().counts(this.lines); },
        get rejectedLines() { return this.lines.filter((l) => l.rejection); },
        get canSave() { return this.hasSheet && !this.saving && this.counts.dirty > 0; },

        // ------------------------------------------------------------------ Feuille d'appel

        async loadDormitories() {
            try {
                this.dormitories = await window.api.get('/boarding/dormitories');
            } catch (err) {
                this.dormitories = [];
                this.error = this.helpers().errorMessage(err, 'Les pavillons n\'ont pas pu être chargés.');
            }
        },

        /**
         * Appelée quand le pavillon ou la nuit change. Une date future est refusée sans appel. S'il reste une saisie non enregistrée, on demande avant de
         * la jeter ; la sélection reste modifiée le temps de la réponse (annuler la remet comme la feuille affichée).
         */
        async onSelectionChange() {
            this.dateError = this.logic().validateDate(this.sel.date, this.helpers().today());
            if (this.dateError || !this.sel.dormitoryId) return;

            if (this.hasSheet && this.logic().hasUnsavedInput(this.lines)) {
                this.discard = { open: true };
                return;
            }
            await this.loadSheet();
        },

        cancelDiscard() {
            this.discard = { open: false };
            if (this.loaded) {
                this.sel.dormitoryId = this.loaded.dormitoryId;
                this.sel.date = this.loaded.date;
                this.dateError = '';
            }
        },

        async confirmDiscard() {
            this.discard = { open: false };
            await this.loadSheet();
        },

        /** Un compteur écarte toute réponse arrivée après une demande plus récente. */
        async loadSheet() {
            const seq = ++this.sheetSeq;
            const { dormitoryId, date } = this.sel;
            this.loading = true;
            this.error = '';
            this.notice = '';
            this.result = null;
            try {
                const sheet = await window.api.get(`/boarding/attendance?dormitoryId=${encodeURIComponent(dormitoryId)}&date=${encodeURIComponent(date)}`);
                if (seq !== this.sheetSeq) return;
                this.lines = this.logic().toLines(sheet);
                this.sheetName = sheet.dormitoryName;
                this.loaded = { dormitoryId, date };
            } catch (err) {
                if (seq !== this.sheetSeq) return;
                this.lines = [];
                this.loaded = null;
                this.error = this.helpers().errorMessage(err, 'Erreur lors du chargement de la feuille d\'appel.');
            } finally {
                if (seq === this.sheetSeq) this.loading = false;
            }
        },

        setPresence(line, present) {
            this.logic().setPresence(line, present);
        },

        markAllPresent() {
            this.logic().markAllPresent(this.lines);
        },

        // ------------------------------------------------------------------ Enregistrement

        async save() {
            if (!this.hasSheet || this.saving) return;

            const entries = this.logic().dirtyEntries(this.lines);
            if (entries.length === 0) {
                this.notice = 'Aucune modification à enregistrer.';
                return;
            }
            const noteError = this.logic().validateNotes(this.lines);
            if (noteError) {
                this.error = noteError;
                return;
            }

            this.saving = true;
            this.error = '';
            this.notice = '';
            try {
                const result = await window.api.post('/boarding/attendance', {
                    date: this.loaded.date, dormitoryId: this.loaded.dormitoryId, entries
                });
                this.logic().applyResult(this.lines, result);
                this.result = { saved: result.saved, rejected: result.rejected };
                this.notice = this.summary(result);
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'Impossible d\'enregistrer le pointage.');
                // Un autre opérateur a saisi la même nuit entre-temps : la feuille affichée est périmée, on la relit.
                if (err && err.code === 'ATTENDANCE_CONFLICT') {
                    const message = this.error;
                    await this.loadSheet();
                    this.error = message;
                }
            } finally {
                this.saving = false;
            }
        },

        /** Phrase de bilan : les succès d'abord, puis les refus s'il y en a. */
        summary(result) {
            const saved = `${result.saved} ligne${result.saved > 1 ? 's' : ''} enregistrée${result.saved > 1 ? 's' : ''}`;
            return result.rejected > 0 ? `${saved}, ${result.rejected} refusée${result.rejected > 1 ? 's' : ''} (voir le détail ci-dessous).` : `${saved}.`;
        },

        // ------------------------------------------------------------------ PDF

        /** Registre imprimable de la nuit affichée. Le serveur répond 422 pour une date hors année active : son message s'affiche dans la modale. */
        async openRegisterPdf() {
            if (!this.loaded) return;
            const helpers = this.helpers();
            await this.openPdfPreview(
                helpers.registerPdfUrl(this.loaded.dormitoryId, this.loaded.date),
                `Registre — ${this.sheetName} — ${this.loaded.date}`,
                helpers.pdfFileName('Registre-Pavillon', this.sheetName, this.loaded.date));
        }
    }));
});
