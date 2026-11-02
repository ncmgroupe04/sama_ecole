/**
 * Onglet « Pointage » de l'écran Internat (/internat) — feuille d'appel d'une SESSION de pointage (appel de nuit, repas, appel d'une Halqa) et
 * pointage collectif, sur /api/v1/boarding (lot G4, tâche 11, puis sessions configurables). Composant Alpine `attendanceTab` ; la logique pure
 * vit dans `window.attendanceLogic` (attendance-logic.js).
 *
 * Données : GET /boarding/attendance/sessions?activeOnly=true (les sessions pointables, dans l'ordre de la Direction) ;
 *   - session des INTERNES (ou école sans session : appel de nuit d'origine) : GET /boarding/attendance?dormitoryId=&date=[&sessionId=] et
 *     POST /boarding/attendance {date, dormitoryId, sessionId?, entries[{boarderId…}]} ; registre imprimable GET /boarding/dormitories/{id}/register-pdf ;
 *   - autre public (demi-pensionnaires, tous les pensionnaires, Halqa) : le groupe (classe ou Oustaz) vient de GET /boarding/attendance/groups?sessionId=,
 *     la feuille de GET /boarding/attendance/sheet?sessionId=&groupId=&date= et l'envoi de POST /boarding/attendance/sheet {sessionId, groupId, date,
 *     entries[{studentId…}]} ;
 *   - récapitulatif du mois : GET /boarding/attendance/monthly-summary?month=&sessionId=&groupId= et son PDF (modale d'aperçu commune).
 * Réponse du pointage LIGNE PAR LIGNE : une ligne refusée n'empêche pas les autres.
 *
 * Règles reflétées (le serveur reste le juge) :
 *   - un pensionnaire en permission ne peut être pointé qu'absent (sinon BOARDER_ON_LEAVE) : la case « présent » lui est verrouillée ;
 *   - le jour n'est jamais dans le futur (refusé avant l'appel) ; hors année scolaire active, le serveur répond 422 et son message est affiché ;
 *   - un jour que la session ne couvre pas est SIGNALÉ, jamais bloqué ;
 *   - seules les lignes DÉCIDÉES et MODIFIÉES sont envoyées ;
 *   - changer de session, de groupe ou de jour avec une saisie non enregistrée demande confirmation : sur 200 lignes, la perdre en silence serait grave.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('attendanceTab', () => ({
        // Modale d'aperçu PDF commune (wwwroot/js/pdf-preview.js) : registre du pavillon et récapitulatif du mois.
        ...window.pdfPreview.state(),

        loading: false,
        loadedAt: 0,
        saving: false,
        error: '',
        dateError: '',
        notice: '',
        sessions: [],
        dormitories: [],
        groups: [],
        // Sélection en cours (liée aux champs) et feuille réellement affichée : elles diffèrent le temps d'une confirmation d'abandon.
        // `dormitoryId` sert aux internes (feuille d'un pavillon), `groupId` aux autres publics (classe ou Oustaz).
        sel: { sessionId: '', dormitoryId: '', groupId: '', date: '' },
        loaded: null,
        sheetName: '',
        outsideDays: false,
        lines: [],
        result: null,
        discard: { open: false },
        sheetSeq: 0,
        // Récapitulatif du mois d'une session pour le groupe affiché.
        summary: { open: false, loading: false, month: '', data: null },
        summaryError: '',

        async init() {
            this.sel.date = window.internatHelpers.today();
            await this.loadSessions();
            await this.loadDormitories();
            await this.applySessionChange();
        },

        helpers() { return window.internatHelpers; },
        logic() { return window.attendanceLogic; },

        // ------------------------------------------------------------------ Session et groupe choisis

        get canConfigure() { return window.auth && window.auth.role === 'Directeur'; },
        get hasSessions() { return this.sessions.length > 0; },
        get sessionOptions() {
            return this.sessions.map((s) => ({ value: s.id, label: s.timeLabel ? `${s.name} · ${s.timeLabel}` : s.name }));
        },
        get selectedSession() { return this.sessions.find((s) => s.id === this.sel.sessionId) || null; },
        /** Public de la session choisie ; sans session (école qui n'en a pas configuré), l'appel de nuit d'origine : les internes d'un pavillon. */
        get audience() { return this.selectedSession ? this.selectedSession.audience : 'Interns'; },
        get isGroup() { return this.logic().isGroupAudience(this.audience); },
        get groupLabel() { return this.logic().groupLabel(this.audience); },
        get dayLabel() { return this.isGroup ? 'Jour' : 'Nuit'; },
        get dormitoryOptions() { return this.dormitories.map((d) => ({ value: d.id, label: d.name })); },
        get groupOptions() { return this.groups.map((g) => ({ value: g.id, label: `${g.name} (${g.studentCount})` })); },
        get currentGroupId() { return this.isGroup ? this.sel.groupId : this.sel.dormitoryId; },
        get hasSheet() { return this.loaded !== null; },
        get counts() { return this.logic().counts(this.lines); },
        get rejectedLines() { return this.lines.filter((l) => l.rejection); },
        get canSave() { return this.hasSheet && !this.saving && this.counts.dirty > 0; },
        /** Le registre PDF existe pour la feuille d'un pavillon seulement ; les autres publics ont le récapitulatif du mois. */
        get canPrintRegister() { return this.hasSheet && this.loaded.audience === 'Interns'; },
        /** Feuille d'un pavillon (chambre · lit) ou d'un groupe (régime) : lue par la vue sans jamais toucher `loaded` quand il est encore null. */
        get isPavilionSheet() { return !this.loaded || this.loaded.audience === 'Interns'; },
        get canSummarize() { return this.hasSheet && !!this.loaded.sessionId; },
        get summaryTotals() { return this.summary.data ? this.logic().summaryTotals(this.summary.data.rows) : null; },
        get summaryTitle() {
            const d = this.summary.data;
            return d ? `${d.sessionName} — ${d.groupName} — ${this.logic().monthLabel(this.summary.month)}` : '';
        },

        // ------------------------------------------------------------------ Chargements

        async loadSessions() {
            try {
                const sessions = await window.api.get('/boarding/attendance/sessions?activeOnly=true');
                this.sessions = Array.isArray(sessions) ? sessions : [];
            } catch (err) {
                this.sessions = [];
                this.error = this.helpers().errorMessage(err, 'Les sessions de pointage n\'ont pas pu être chargées.');
            }

            // Une session choisie qui n'existe plus (désactivée entre-temps) ne reste pas sélectionnée ; sinon, la première est proposée.
            if (!this.selectedSession) this.sel.sessionId = this.sessions.length > 0 ? this.sessions[0].id : '';
        },

        async loadDormitories() {
            try {
                this.dormitories = await window.api.get('/boarding/dormitories');
            } catch (err) {
                this.dormitories = [];
                this.error = this.helpers().errorMessage(err, 'Les pavillons n\'ont pas pu être chargés.');
            }
        },

        async loadGroups() {
            if (!this.sel.sessionId || !this.isGroup) {
                this.groups = [];
                return;
            }

            const sessionId = this.sel.sessionId;
            try {
                const groups = await window.api.get(`/boarding/attendance/groups?sessionId=${encodeURIComponent(sessionId)}`);
                if (sessionId !== this.sel.sessionId) return;
                this.groups = Array.isArray(groups) ? groups : [];
            } catch (err) {
                this.groups = [];
                this.error = this.helpers().errorMessage(err, `Les groupes (${this.groupLabel.toLowerCase()}) n'ont pas pu être chargés.`);
            }
        },

        /**
         * Applique la session choisie : vide la feuille et le groupe, charge les groupes de son public, et ouvre tout seul la feuille quand il n'y a
         * qu'un choix (un seul pavillon, une seule classe, un seul Oustaz).
         */
        async applySessionChange() {
            this.loaded = null;
            this.lines = [];
            this.result = null;
            this.notice = '';
            this.outsideDays = false;
            this.sel.groupId = '';

            if (this.isGroup) {
                await this.loadGroups();
                if (this.groups.length === 1) this.sel.groupId = this.groups[0].id;
            } else if (this.dormitories.length === 1) {
                this.sel.dormitoryId = this.dormitories[0].id;
            }

            if (this.currentGroupId) await this.onSelectionChange();
        },

        /** Le sélecteur de session a changé : une saisie non enregistrée demande d'abord confirmation. */
        async onSessionChange() {
            if (this.hasSheet && this.logic().hasUnsavedInput(this.lines)) {
                this.discard = { open: true };
                return;
            }
            await this.applySessionChange();
        },

        /**
         * Appelée quand le groupe ou le jour change. Une date future est refusée sans appel. S'il reste une saisie non enregistrée, on demande avant de
         * la jeter ; la sélection reste modifiée le temps de la réponse (annuler la remet comme la feuille affichée).
         */
        async onSelectionChange() {
            this.dateError = this.logic().validateDate(this.sel.date, this.helpers().today());
            if (this.dateError || !this.currentGroupId) return;

            if (this.hasSheet && this.logic().hasUnsavedInput(this.lines)) {
                this.discard = { open: true };
                return;
            }
            await this.loadSheet();
        },

        cancelDiscard() {
            this.discard = { open: false };
            if (this.loaded) {
                this.sel.sessionId = this.loaded.sessionId || this.sel.sessionId;
                if (this.loaded.audience === 'Interns') this.sel.dormitoryId = this.loaded.dormitoryId;
                else this.sel.groupId = this.loaded.groupId;
                this.sel.date = this.loaded.date;
                this.dateError = '';
            }
        },

        async confirmDiscard() {
            this.discard = { open: false };
            if (this.loaded && this.sel.sessionId !== (this.loaded.sessionId || '')) {
                await this.applySessionChange();
                return;
            }
            await this.loadSheet();
        },

        // ------------------------------------------------------------------ Feuille d'appel

        /** Un compteur écarte toute réponse arrivée après une demande plus récente. */
        async loadSheet() {
            const seq = ++this.sheetSeq;
            const { sessionId, date } = this.sel;
            const audience = this.audience;
            const group = this.isGroup;
            const groupId = this.currentGroupId;
            this.loading = true;
            this.error = '';
            this.notice = '';
            this.result = null;
            try {
                const day = encodeURIComponent(date);
                const sheet = group
                    ? await window.api.get(`/boarding/attendance/sheet?sessionId=${encodeURIComponent(sessionId)}&groupId=${encodeURIComponent(groupId)}&date=${day}`)
                    : await window.api.get(`/boarding/attendance?dormitoryId=${encodeURIComponent(groupId)}&date=${day}${sessionId ? `&sessionId=${encodeURIComponent(sessionId)}` : ''}`);
                if (seq !== this.sheetSeq) return;
                this.lines = group ? this.logic().toGroupLines(sheet) : this.logic().toLines(sheet);
                this.sheetName = group ? sheet.groupName : sheet.dormitoryName;
                this.outsideDays = !!sheet.outsideSessionDays;
                this.loaded = group
                    ? { sessionId, audience, groupId, dormitoryId: '', date }
                    : { sessionId, audience, groupId: '', dormitoryId: groupId, date };
            } catch (err) {
                if (seq !== this.sheetSeq) return;
                this.lines = [];
                this.loaded = null;
                this.error = this.helpers().errorMessage(err, 'Erreur lors du chargement de la feuille d\'appel.');
            } finally {
                if (seq === this.sheetSeq) {
                    this.loading = false;
                    this.loadedAt = Date.now();
                }
            }
        },

        /**
         * Relit à l'ouverture de l'onglet : une sortie déclarée dans « Sorties » doit verrouiller « absent » le pensionnaire concerné dès maintenant, pas au
         * prochain rechargement de la page. JAMAIS pendant une saisie non enregistrée, un enregistrement, une confirmation d'abandon, un récapitulatif ou un
         * aperçu PDF : relire jetterait les pointages saisis. Sans feuille affichée, on relit les sessions et les pavillons (l'un d'eux a pu être créé
         * entre-temps).
         */
        async onTabShown(key) {
            const busy = this.loading || this.saving || this.discard.open || this.summary.open || this.showPdfModal
                || (this.hasSheet && this.logic().hasUnsavedInput(this.lines));
            if (!this.helpers().shouldRefreshOnShow({ tabKey: 'pointage', shownKey: key, busy, loadedAt: this.loadedAt, now: Date.now() })) return;

            if (this.hasSheet) {
                await this.loadSheet();
                return;
            }
            await this.reloadChoices();
        },

        /** La Direction a (dés)activé, créé ou réordonné des sessions : on relit la liste sans toucher à une saisie en cours. */
        async onSessionsChanged() {
            if (this.hasSheet && this.logic().hasUnsavedInput(this.lines)) {
                // La feuille affichée reste ; la liste des sessions se met à jour sans changer la sélection si elle existe encore.
                await this.loadSessions();
                return;
            }
            await this.reloadChoices();
        },

        async reloadChoices() {
            const previous = this.sel.sessionId;
            await this.loadSessions();
            await this.loadDormitories();
            if (this.sel.sessionId !== previous || !this.hasSheet) await this.applySessionChange();
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

            const group = this.loaded.audience !== 'Interns';
            const entries = group ? this.logic().dirtyGroupEntries(this.lines) : this.logic().dirtyEntries(this.lines);
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
                const result = group
                    ? await window.api.post('/boarding/attendance/sheet', {
                        sessionId: this.loaded.sessionId, groupId: this.loaded.groupId, date: this.loaded.date, entries
                    })
                    : await window.api.post('/boarding/attendance', {
                        date: this.loaded.date, dormitoryId: this.loaded.dormitoryId,
                        ...(this.loaded.sessionId ? { sessionId: this.loaded.sessionId } : {}), entries
                    });
                this.logic().applyResult(this.lines, result);
                this.result = { saved: result.saved, rejected: result.rejected };
                this.notice = this.summaryOf(result);
                // Une école sans session vient d'en obtenir une (« Appel de nuit », créée à la volée) : on la propose dès maintenant.
                if (!this.hasSessions && !group) await this.loadSessions();
            } catch (err) {
                this.error = this.helpers().errorMessage(err, 'Impossible d\'enregistrer le pointage.');
                // Un autre opérateur a saisi la même session le même jour entre-temps : la feuille affichée est périmée, on la relit.
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
        summaryOf(result) {
            const saved = `${result.saved} ligne${result.saved > 1 ? 's' : ''} enregistrée${result.saved > 1 ? 's' : ''}`;
            return result.rejected > 0 ? `${saved}, ${result.rejected} refusée${result.rejected > 1 ? 's' : ''} (voir le détail ci-dessous).` : `${saved}.`;
        },

        // ------------------------------------------------------------------ PDF et récapitulatif du mois

        /** Registre imprimable de la nuit affichée. Le serveur répond 422 pour une date hors année active : son message s'affiche dans la modale. */
        async openRegisterPdf() {
            if (!this.canPrintRegister) return;
            const helpers = this.helpers();
            await this.openPdfPreview(
                helpers.registerPdfUrl(this.loaded.dormitoryId, this.loaded.date, this.loaded.sessionId),
                `Registre — ${this.sheetName} — ${this.loaded.date}`,
                helpers.pdfFileName('Registre-Pavillon', this.sheetName, this.loaded.date));
        },

        /** Ouvre le récapitulatif du mois de la feuille affichée (le mois du jour pointé). */
        async openSummary() {
            if (!this.canSummarize) return;
            this.summary = { open: true, loading: false, month: this.logic().monthOf(this.loaded.date), data: null };
            this.summaryError = '';
            await this.loadSummary();
        },

        closeSummary() {
            this.summary.open = false;
        },

        async loadSummary() {
            if (!this.loaded) return;
            const url = this.helpers().monthlySummaryUrl(this.summary.month, this.loaded.sessionId, this.currentLoadedGroup());
            if (!url) {
                this.summaryError = 'Indiquez un mois.';
                this.summary.data = null;
                return;
            }
            this.summary.loading = true;
            this.summaryError = '';
            try {
                this.summary.data = await window.api.get(url.replace(/^\/api\/v1/, ''));
            } catch (err) {
                this.summary.data = null;
                this.summaryError = this.helpers().errorMessage(err, 'Le récapitulatif n\'a pas pu être chargé.');
            } finally {
                this.summary.loading = false;
            }
        },

        currentLoadedGroup() {
            return this.loaded ? (this.loaded.audience === 'Interns' ? this.loaded.dormitoryId : this.loaded.groupId) : '';
        },

        async openSummaryPdf() {
            if (!this.loaded || !this.summary.data) return;
            const helpers = this.helpers();
            await this.openPdfPreview(
                helpers.monthlySummaryUrl(this.summary.month, this.loaded.sessionId, this.currentLoadedGroup(), true),
                `Récapitulatif — ${this.summary.data.sessionName} — ${this.summary.data.groupName} — ${this.summary.month}`,
                helpers.pdfFileName('Recapitulatif-Pointage', this.summary.data.sessionName, this.summary.data.groupName, this.summary.month));
        }
    }));
});
