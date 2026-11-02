/**
 * Appel de la Halqa — espace de l'Oustaz (/halqa, arabe, de droite à gauche, tablette). Composant Alpine `halqaRoll`, posé dans la page `halqaPage`
 * (Views/Internat/_HalqaRoll.cshtml). L'Oustaz fait l'appel de SA Halqa pour une session que la Direction a créée et activée
 * (« Appel de la Halqa »…) : il coche présent ou absent pour chaque élève et enregistre.
 *
 * Données : GET /internat/attendance/sessions (sessions de Halqa ACTIVES), GET /internat/attendance/sheet?sessionId=&date= (la Halqa de l'Oustaz : le
 * groupe est imposé par le serveur), POST /internat/attendance {sessionId, date, entries[{studentId, isPresent, note}]}. Réponse LIGNE PAR LIGNE.
 *
 * Même logique pure que l'onglet « Pointage » de l'Internat (`window.attendanceLogic`) : seules les lignes décidées ET modifiées partent, un résultat
 * mixte garde chaque refus visible, la date n'est jamais dans le futur. Le serveur reste le juge. Le bouton n'apparaît que s'il existe au moins une
 * session de Halqa active : sans elle, l'Oustaz n'a rien à pointer et l'écran ne lui montre rien d'inutile.
 */
window.halqaRollLabels = {
    loadError: 'تعذّر تحميل جلسات النداء.',
    sheetError: 'تعذّر تحميل ورقة النداء.',
    saveError: 'تعذّر حفظ النداء.',
    noChange: 'لا يوجد ما يُحفظ.',
    futureDate: 'لا يمكن تسجيل النداء في تاريخ مستقبلي.',
    emptyDate: 'اختر التاريخ.',
    saved: (n) => `تم حفظ ${n} سطرًا.`,
    savedWithRejected: (n, r) => `تم حفظ ${n} سطرًا، ورُفض ${r}.`,
    inactive: 'هذه الجلسة غير مفعّلة. اطلب من المدير تفعيلها.',
    conflict: 'سجّل شخص آخر هذا النداء قبلك. تم تحديث الورقة، راجعها ثم أعد المحاولة.',
    rejections: {
        NOT_IN_GROUP: 'هذا الطالب لم يعد في حلقتك. حدّث الورقة.',
        DUPLICATE_ENTRY: 'ورد هذا الطالب مرتين.',
        BOARDER_ON_LEAVE: 'هذا الطالب في إجازة: لا يمكن تسجيله حاضرًا.'
    }
};

document.addEventListener('alpine:init', () => {
    Alpine.data('halqaRoll', () => ({
        sessions: [],
        sessionId: '',
        date: '',
        isOpen: false,
        loading: false,
        saving: false,
        error: '',
        notice: '',
        dateError: '',
        outsideDays: false,
        lines: [],
        loaded: null,
        result: null,
        sheetSeq: 0,
        labels: window.halqaRollLabels,

        async init() {
            this.date = this.today();
            await this.loadSessions();
        },

        logic() { return window.attendanceLogic; },

        /** Aujourd'hui, au format AAAA-MM-JJ, lu en heure LOCALE (toISOString décalerait le jour la nuit). */
        today() {
            const now = new Date();
            const pad = (n) => String(n).padStart(2, '0');
            return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
        },

        get canRoll() { return this.sessions.length > 0; },
        get selectedSession() { return this.sessions.find((s) => s.id === this.sessionId) || null; },
        get hasSheet() { return this.loaded !== null; },
        get counts() { return this.logic().counts(this.lines); },
        get rejectedLines() { return this.lines.filter((l) => l.rejection); },
        get canSave() { return this.hasSheet && !this.saving && this.counts.dirty > 0; },

        displayName(line) { return line.studentNameAr || line.studentName; },

        // ------------------------------------------------------------------ Sessions

        async loadSessions() {
            try {
                const sessions = await window.api.get('/internat/attendance/sessions');
                this.sessions = Array.isArray(sessions) ? sessions : [];
            } catch (err) {
                // silence-volontaire : l'appel est un plus. Un échec (module désactivé, réseau) ne doit pas gêner le suivi coranique de l'Oustaz ;
                // la page affiche déjà ses propres erreurs, et le bouton « إجراء النداء » reste simplement absent.
                this.sessions = [];
            }
            if (!this.selectedSession) this.sessionId = this.sessions.length > 0 ? this.sessions[0].id : '';
        },

        // ------------------------------------------------------------------ Fenêtre d'appel

        async openRoll() {
            if (!this.canRoll) return;
            this.isOpen = true;
            this.notice = '';
            await this.loadSheet();
        },

        closeRoll() {
            this.isOpen = false;
        },

        /** Changer de session ou de jour rechargerait la feuille : une saisie non enregistrée n'est jamais jetée en silence. */
        async onSelectionChange() {
            const problem = this.logic().validateDate(this.date, this.today());
            this.dateError = problem ? this.dateMessage() : '';
            if (this.dateError || !this.sessionId) return;
            if (this.hasSheet && this.logic().hasUnsavedInput(this.lines)
                && !window.confirm('توجد تسجيلات غير محفوظة. هل تريد تجاهلها؟')) {
                this.sessionId = this.loaded.sessionId;
                this.date = this.loaded.date;
                this.dateError = '';
                return;
            }
            await this.loadSheet();
        },

        dateMessage() {
            return this.date ? this.labels.futureDate : this.labels.emptyDate;
        },

        /** Un compteur écarte toute réponse arrivée après une demande plus récente. */
        async loadSheet() {
            if (!this.sessionId) return;
            const seq = ++this.sheetSeq;
            const { sessionId, date } = this;
            this.loading = true;
            this.error = '';
            this.notice = '';
            this.result = null;
            try {
                const sheet = await window.api.get(`/internat/attendance/sheet?sessionId=${encodeURIComponent(sessionId)}&date=${encodeURIComponent(date)}`);
                if (seq !== this.sheetSeq) return;
                this.lines = this.logic().toGroupLines(sheet);
                this.outsideDays = !!sheet.outsideSessionDays;
                this.loaded = { sessionId, date };
            } catch (err) {
                if (seq !== this.sheetSeq) return;
                this.lines = [];
                this.loaded = null;
                this.error = this.messageOf(err, this.labels.sheetError);
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

            const entries = this.logic().dirtyGroupEntries(this.lines);
            if (entries.length === 0) {
                this.notice = this.labels.noChange;
                return;
            }

            this.saving = true;
            this.error = '';
            this.notice = '';
            try {
                const result = await window.api.post('/internat/attendance', {
                    sessionId: this.loaded.sessionId, date: this.loaded.date, entries
                });
                this.logic().applyResult(this.lines, result);
                // Messages de rejet en arabe : l'écran est de droite à gauche, pas ceux (français) de la feuille de l'Internat.
                this.lines.forEach((l) => {
                    if (l.rejection) l.rejection = this.rejectionText(l.rejection);
                });
                this.result = { saved: result.saved, rejected: result.rejected };
                this.notice = result.rejected > 0 ? this.labels.savedWithRejected(result.saved, result.rejected) : this.labels.saved(result.saved);
            } catch (err) {
                this.error = this.messageOf(err, this.labels.saveError);
                if (err && err.code === 'ATTENDANCE_CONFLICT') {
                    const message = this.error;
                    await this.loadSheet();
                    this.error = message;
                }
            } finally {
                this.saving = false;
            }
        },

        /** Rejet de ligne : le texte français de la logique partagée est remplacé par son équivalent arabe quand le code est connu. */
        rejectionText(frenchMessage) {
            const logic = this.logic();
            const code = Object.keys(this.labels.rejections).find((c) => logic.rejectionMessage(c, '') === frenchMessage);
            return code ? this.labels.rejections[code] : frenchMessage;
        },

        messageOf(err, fallback) {
            if (err && err.handledGlobally) return '';
            if (err && err.code === 'ATTENDANCE_SESSION_INACTIVE') return this.labels.inactive;
            if (err && err.code === 'ATTENDANCE_CONFLICT') return this.labels.conflict;
            return (window.api && window.api.toMessage ? window.api.toMessage(err, fallback) : (err && err.message)) || fallback;
        }
    }));
});
