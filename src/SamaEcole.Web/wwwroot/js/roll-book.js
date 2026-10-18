/**
 * Cahier de roulement / registre d'appel bilingue — Franco-Arabe Bloc 5b (/cahier-de-roulement).
 *
 * Une grille mensuelle par classe : un élève par ligne, deux demi-journées (matin / après-midi) par jour de
 * classe. Chaque case fusionne la SAISIE du cahier (prioritaire) et le statut DÉRIVÉ des appels par séance — le
 * serveur décide laquelle l'emporte et renvoie `source` (Entry / Derived). Ici on édite des CASES EN ATTENTE
 * (`pending`) qu'on envoie en un lot atomique (PUT /roll-book/entries) ; `status: null` = revenir au calcul.
 *
 * Le serveur reste seul juge : portée de l'Enseignant (403), dates futures ou hors année (422), élève hors classe
 * (422), taux et couverture. Les contrôles faits ici (cases futures désactivées…) ne servent qu'au confort.
 *
 * Affichage FR / AR / bilingue PROPRE À L'ÉCRAN (pas de mécanisme i18n global dans l'application) : un objet de
 * libellés, et `dir="rtl"` sur la grille en arabe. Les libellés arabes sont à faire relire par un locuteur natif.
 */
(function () {
    // Ordre de rotation au clic : vide → présent → retard → absent justifié → absent non justifié → retour au calcul.
    const NEXT_STATUS = {
        NotRecorded: 'Present',
        Present: 'Late',
        Late: 'JustifiedAbsence',
        JustifiedAbsence: 'UnjustifiedAbsence',
        UnjustifiedAbsence: null,
        // Une absence partielle est un constat dérivé, jamais une saisie : le clic la remplace par « présent ».
        PartialAbsence: 'Present'
    };

    const KEY_TO_STATUS = { p: 'Present', r: 'Late', j: 'JustifiedAbsence', a: 'UnjustifiedAbsence' };

    const LABELS = {
        title: { fr: 'Cahier de roulement', ar: 'سجل الحضور والغياب' },
        grid: { fr: 'Grille', ar: 'الجدول' },
        stats: { fr: 'Statistiques', ar: 'الإحصائيات' },
        classroom: { fr: 'Classe', ar: 'القسم' },
        month: { fr: 'Mois', ar: 'الشهر' },
        language: { fr: 'Langue', ar: 'اللغة' },
        student: { fr: 'Élève', ar: 'التلميذ' },
        morning: { fr: 'Matin', ar: 'صباحا' },
        afternoon: { fr: 'Après-midi', ar: 'مساء' },
        morningShort: { fr: 'M', ar: 'ص' },
        afternoonShort: { fr: 'S', ar: 'م' },
        present: { fr: 'Présent', ar: 'حاضر' },
        late: { fr: 'Retard', ar: 'متأخر' },
        partial: { fr: 'Absence partielle', ar: 'غياب جزئي' },
        justified: { fr: 'Absent justifié', ar: 'غائب بعذر' },
        unjustified: { fr: 'Absent non justifié', ar: 'غائب بدون عذر' },
        notRecorded: { fr: 'Non renseigné', ar: 'غير مسجل' },
        entry: { fr: 'Saisie du cahier', ar: 'إدخال يدوي' },
        exportPdf: { fr: 'Exporter en PDF', ar: 'تصدير PDF' },
        revert: { fr: 'Revenir au calcul', ar: 'العودة إلى الحساب التلقائي' },
        save: { fr: 'Enregistrer', ar: 'حفظ' },
        cancel: { fr: 'Annuler les modifications', ar: 'إلغاء التعديلات' },
        legend: { fr: 'Légende', ar: 'دليل الرموز' },
        presenceRate: { fr: 'Taux de présence', ar: 'نسبة الحضور' },
        absenceRate: { fr: "Taux d'absence", ar: 'نسبة الغياب' },
        assiduityRate: { fr: "Taux d'assiduité", ar: 'نسبة المواظبة' },
        coverage: { fr: 'Couverture', ar: 'نسبة التسجيل' },
        classDays: { fr: 'jours de classe', ar: 'أيام الدراسة' },
        level: { fr: 'Niveau', ar: 'المستوى' },
        students: { fr: 'Élèves', ar: 'التلاميذ' },
        total: { fr: 'Total', ar: 'المجموع' }
    };

    // Glyphes de case. En arabe : ✓ / ت (تأخر) / ع (عذر) / غ (غياب) / ½ — abréviations à valider par un locuteur natif.
    const GLYPHS = {
        fr: { Present: '✓', Late: 'R', PartialAbsence: '½', JustifiedAbsence: 'AJ', UnjustifiedAbsence: 'A', NotRecorded: '·' },
        ar: { Present: '✓', Late: 'ت', PartialAbsence: '½', JustifiedAbsence: 'ع', UnjustifiedAbsence: 'غ', NotRecorded: '·' }
    };

    const MARK_CLASSES = {
        Present: 'bg-emerald-50 text-emerald-700',
        Late: 'bg-amber-50 text-amber-700',
        PartialAbsence: 'bg-yellow-50 text-yellow-700',
        JustifiedAbsence: 'bg-sky-50 text-sky-700',
        UnjustifiedAbsence: 'bg-red-50 text-red-700',
        NotRecorded: 'text-slate-300'
    };

    const MARK_LABEL_KEYS = {
        Present: 'present', Late: 'late', PartialAbsence: 'partial', JustifiedAbsence: 'justified',
        UnjustifiedAbsence: 'unjustified', NotRecorded: 'notRecorded'
    };

    const cellKey = (studentId, date, half) => `${studentId}|${date}|${half}`;

    /** Statut suivant dans la rotation de clic ; `null` = revenir au calcul. */
    function nextStatus(mark) {
        return Object.prototype.hasOwnProperty.call(NEXT_STATUS, mark) ? NEXT_STATUS[mark] : 'Present';
    }

    /** Taux 0..1 → « 87,5 % » (virgule française) ; null → « — » (jamais un faux 100 %). */
    function percent(rate) {
        if (rate === null || rate === undefined) return '—';
        return `${(Math.round(rate * 1000) / 10).toString().replace('.', ',')} %`;
    }

    /** « 2026-11 » du jour donné, en UTC (le Sénégal est à UTC+0 toute l'année). */
    function currentMonth(now = new Date()) {
        return now.toISOString().slice(0, 7);
    }

    window.rollBookLogic = { NEXT_STATUS, LABELS, GLYPHS, cellKey, nextStatus, percent, currentMonth };

    document.addEventListener('alpine:init', () => {
        Alpine.data('rollBookView', () => ({
            // Aperçu PDF partagé (pdf-preview.js) : impression ou téléchargement au choix, jamais un download forcé.
            ...(window.pdfPreview ? window.pdfPreview.state() : {}),

            classrooms: [],
            terms: [],
            classroomId: '',
            month: currentMonth(),
            tab: 'grid',

            isLoading: false,
            isSaving: false,
            error: null,
            notice: null,

            grid: null,
            cellIndex: new Map(),
            pending: {},
            selected: null,

            // Statistiques.
            statsScope: 'class',
            statsPeriod: 'month',
            statsTermId: '',
            stats: null,
            isLoadingStats: false,

            /** Choix de l'utilisateur (« fr », « ar », « bi »), ou null : la langue effective se déduit alors du module. */
            langChoice: null,
            today: new Date().toISOString().slice(0, 10),

            /** Confort d'affichage : l'API refuse (403) l'export à l'Enseignant, qui saisit le cahier sans l'exporter. */
            canExport: ['Directeur', 'Secretariat', 'Surveillant', 'SuperAdmin'].includes(window.auth && window.auth.role),

            /** Confort d'affichage : l'API refuse (403) les statistiques aux autres rôles. */
            canStats: ['Directeur', 'Secretariat', 'SuperAdmin'].includes(window.auth && window.auth.role),

            get bilingualAvailable() {
                const config = Alpine.store('schoolConfig');
                return !!(config && config.coranEnabled);
            },

            /**
             * Langue EFFECTIVE de l'écran. Déduite à chaque lecture, jamais figée au chargement : le store `schoolConfig`
             * se remplit après le montage du composant, et une langue fixée trop tôt resterait « fr » pour une école
             * Franco-Arabe (export PDF compris). Sans le module, toujours le français ; avec, le choix mémorisé ou le bilingue.
             */
            get lang() {
                if (!this.bilingualAvailable) return 'fr';
                return ['fr', 'ar', 'bi'].includes(this.langChoice) ? this.langChoice : 'bi';
            },

            get rtl() { return this.lang === 'ar'; },

            get pendingCount() { return Object.keys(this.pending).length; },

            get classDays() { return this.grid ? this.grid.days.filter((d) => d.isClassDay) : []; },

            /**
             * Colonnes à plat de la grille : deux par jour de classe (matin, après-midi), une seule, grisée, par jour
             * non travaillé. À plat parce qu'un modèle Alpine ne peut pas imbriquer deux boucles sur la même ligne.
             */
            get columns() {
                if (!this.grid) return [];
                return this.grid.days.flatMap((day) => day.isClassDay
                    ? [
                        { key: `${day.date}-M`, date: day.date, half: 'Morning', day, off: false },
                        { key: `${day.date}-A`, date: day.date, half: 'Afternoon', day, off: false }
                    ]
                    : [{ key: `${day.date}-O`, date: day.date, half: null, day, off: true }]);
            },

            /** Une saisie existe un jour non travaillé : la grille la signale sans la compter dans les taux. */
            offHasEntry(student, date) {
                return student.cells.some((c) => c.date === date);
            },

            async init() {
                // Langue : choix mémorisé par viewer (confort) ; l'arabe et le bilingue n'existent que si le module Franco-Arabe est actif.
                try { this.langChoice = localStorage.getItem('rollbook.lang'); } catch (e) { /* stockage indisponible */ }

                try {
                    this.classrooms = await window.api.get('/classrooms');
                    const years = await window.api.get('/school-years');
                    const active = years.find((y) => y.isActive);
                    this.terms = active ? await window.api.get(`/school-years/${active.id}/terms`) : [];
                } catch (err) {
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement des classes.');
                }
            },

            // ------------------------------------------------------------------------------ libellés

            /** Libellé dans la langue de l'écran ; en bilingue, « français · arabe ». */
            t(key) {
                const label = LABELS[key];
                if (!label) return key;
                if (this.lang === 'ar') return label.ar;
                if (this.lang === 'bi') return `${label.fr} · ${label.ar}`;
                return label.fr;
            },

            /** Libellé COURT d'en-tête : une seule langue (le bilingue prend le français, faute de place). */
            short(key) {
                const label = LABELS[key];
                return label ? (this.lang === 'ar' ? label.ar : label.fr) : key;
            },

            setLang(lang) {
                this.langChoice = lang;
                try { localStorage.setItem('rollbook.lang', lang); } catch (e) { /* confort uniquement */ }
            },

            glyph(mark) {
                return (GLYPHS[this.lang === 'ar' ? 'ar' : 'fr'])[mark] || '·';
            },

            markClass(mark) { return MARK_CLASSES[mark] || ''; },

            markLabel(mark) { return this.t(MARK_LABEL_KEYS[mark] || 'notRecorded'); },

            /** Nom d'élève : arabe d'abord en arabe, avec repli sur le français ; en bilingue, les deux. */
            studentName(student) {
                if (this.lang === 'ar') return student.fullNameAr || student.fullName;
                return student.fullName;
            },

            dayHeader(day) {
                const locale = this.lang === 'ar' ? 'ar-u-nu-latn' : 'fr-FR';
                const weekday = new Intl.DateTimeFormat(locale, { weekday: 'narrow', timeZone: 'UTC' })
                    .format(new Date(`${day.date}T00:00:00Z`));
                return { weekday, number: Number(day.date.slice(8, 10)) };
            },

            monthTitle() {
                if (!this.grid) return '';
                const locale = this.lang === 'ar' ? 'ar-u-nu-latn' : 'fr-FR';
                return new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric', timeZone: 'UTC' })
                    .format(new Date(`${this.grid.from}T00:00:00Z`));
            },

            offLabel(day) {
                if (day.offReason === 'Closure') {
                    return (this.lang === 'ar' && day.closureLabelAr) ? day.closureLabelAr : (day.closureLabel || '');
                }
                return day.offReason === 'RestDay' ? (this.lang === 'ar' ? 'راحة' : 'Repos') : '';
            },

            percent,

            // --------------------------------------------------------------------------------- chargement

            async loadGrid() {
                if (!this.classroomId || !this.month) return;
                this.isLoading = true;
                this.error = null;
                this.notice = null;
                try {
                    const grid = await window.api.get(
                        `/roll-book?classroomId=${this.classroomId}&month=${this.month}`);
                    this.grid = grid;
                    this.cellIndex = new Map();
                    grid.students.forEach((s) => s.cells.forEach((c) => {
                        this.cellIndex.set(cellKey(s.studentId, c.date, c.halfDay), c);
                    }));
                    this.pending = {};
                    this.selected = null;
                } catch (err) {
                    this.grid = null;
                    this.error = window.api.toMessage(err, 'Erreur lors du chargement du cahier.');
                } finally {
                    this.isLoading = false;
                }
            },

            // ------------------------------------------------------------------------------------ cases

            saved(studentId, date, half) {
                return this.cellIndex.get(cellKey(studentId, date, half)) || null;
            },

            /** La case telle qu'affichée : saisie en attente, sinon valeur serveur (saisie du cahier ou dérivée). */
            effective(studentId, date, half) {
                const key = cellKey(studentId, date, half);
                const pending = this.pending[key];
                if (pending) {
                    return {
                        mark: pending.status || 'NotRecorded', source: 'Pending', pending: true,
                        reverting: pending.status === null,
                        lateMinutes: pending.lateMinutes || 0, reason: pending.reason || ''
                    };
                }
                const saved = this.saved(studentId, date, half);
                return saved
                    ? { mark: saved.mark, source: saved.source, pending: false, lateMinutes: saved.lateMinutes, reason: saved.reason || '', entryId: saved.entryId }
                    : { mark: 'NotRecorded', source: 'None', pending: false, lateMinutes: 0, reason: '' };
            },

            isFuture(date) { return date > this.today; },

            /**
             * Pose (ou retire) une saisie en attente. Revenir à ce que le serveur a déjà ne laisse aucune trace ;
             * « retour au calcul » sur une case sans saisie n'a rien à défaire.
             */
            setCell(studentId, date, half, status, extra = {}) {
                const key = cellKey(studentId, date, half);
                const saved = this.saved(studentId, date, half);
                const hasEntry = !!(saved && saved.source === 'Entry');
                const lateMinutes = status === 'Late' ? (Number(extra.lateMinutes) || 0) : 0;
                const reason = (extra.reason || '').trim();

                const sameAsSaved = status === null
                    ? !hasEntry
                    : hasEntry && saved.mark === status && saved.lateMinutes === lateMinutes && (saved.reason || '') === reason;

                if (sameAsSaved) {
                    delete this.pending[key];
                } else {
                    this.pending[key] = { studentId, date, halfDay: half, status, lateMinutes, reason: reason || null };
                }
            },

            select(studentId, date, half) {
                this.selected = { studentId, date, half };
            },

            /** Clic : sélectionne la case et la fait tourner au statut suivant. */
            cycle(studentId, date, half) {
                if (this.isFuture(date)) return;
                this.select(studentId, date, half);
                const current = this.effective(studentId, date, half);
                const extra = { lateMinutes: current.lateMinutes, reason: current.reason };
                this.setCell(studentId, date, half, nextStatus(current.mark), extra);
            },

            /** Clavier : P / R / J / A posent un statut, Retour arrière / Suppr reviennent au calcul. */
            onKey(event, studentId, date, half) {
                if (this.isFuture(date)) return;
                const key = (event.key || '').toLowerCase();
                if (KEY_TO_STATUS[key]) {
                    event.preventDefault();
                    this.select(studentId, date, half);
                    const current = this.effective(studentId, date, half);
                    this.setCell(studentId, date, half, KEY_TO_STATUS[key], { lateMinutes: current.lateMinutes, reason: current.reason });
                } else if (event.key === 'Backspace' || event.key === 'Delete') {
                    event.preventDefault();
                    this.select(studentId, date, half);
                    this.setCell(studentId, date, half, null);
                }
            },

            get selectedCell() {
                if (!this.selected) return null;
                const { studentId, date, half } = this.selected;
                return { ...this.effective(studentId, date, half), studentId, date, half };
            },

            get selectedStudent() {
                if (!this.selected || !this.grid) return null;
                return this.grid.students.find((s) => s.studentId === this.selected.studentId) || null;
            },

            /** Panneau de détail : fixe explicitement le statut, la durée du retard et le motif. */
            applyDetail(status, lateMinutes, reason) {
                if (!this.selected) return;
                const { studentId, date, half } = this.selected;
                this.setCell(studentId, date, half, status, { lateMinutes, reason });
            },

            discard() {
                this.pending = {};
            },

            // ------------------------------------------------------------------------------ enregistrement

            async save() {
                if (this.pendingCount === 0 || this.isSaving) return;
                this.isSaving = true;
                this.error = null;
                try {
                    const entries = Object.values(this.pending).map((p) => ({
                        studentId: p.studentId,
                        date: p.date,
                        halfDay: p.halfDay,
                        status: p.status,
                        lateMinutes: p.status === 'Late' ? (p.lateMinutes || 0) : 0,
                        reason: p.reason || null
                    }));

                    const result = await window.api.put('/roll-book/entries', { classroomId: this.classroomId, entries });
                    await this.loadGrid();
                    // Seuls les compteurs non nuls sont annoncés : « 0 case(s) enregistrée(s) » n'apprend rien.
                    const parts = [];
                    if (result.created + result.updated > 0) parts.push(`${result.created + result.updated} case(s) enregistrée(s)`);
                    if (result.reverted > 0) parts.push(`${result.reverted} remise(s) au calcul`);
                    this.notice = (parts.join(', ') || 'Aucun changement') + '.';
                } catch (err) {
                    // Le lot est atomique : en cas de refus rien n'est écrit, les modifications restent à l'écran.
                    this.error = window.api.toMessage(err, "Erreur lors de l'enregistrement du cahier.");
                } finally {
                    this.isSaving = false;
                }
            },

            // ------------------------------------------------------------------------------------ export PDF

            /**
             * URL de l'export : un mois (AAAA-MM) OU une période d'évaluation, dans la langue de l'écran (« fr » quand
             * le module Franco-Arabe n'est pas actif — le serveur refuse de toute façon l'arabe et le bilingue, 422).
             */
            pdfUrl({ termId = null } = {}) {
                const params = new URLSearchParams({ classroomId: this.classroomId });
                if (termId) params.set('termId', termId);
                else params.set('month', this.month);
                params.set('lang', this.lang);
                return `/api/v1/roll-book/pdf?${params.toString()}`;
            },

            /** Le PDF relit la base : les modifications en attente n'y seraient pas, on refuse donc d'exporter avec elles. */
            get exportBlocked() { return this.pendingCount > 0; },

            exportMonthPdf() {
                if (!this.canExport || !this.classroomId || !this.month || this.exportBlocked) return null;
                return this.openPdfPreview(this.pdfUrl(), 'Cahier de roulement', `Cahier-de-roulement-${this.month}.pdf`);
            },

            get canExportTerm() {
                return this.canExport && this.statsScope === 'class' && this.statsPeriod === 'term'
                    && !!this.statsTermId && !!this.classroomId;
            },

            exportTermPdf() {
                if (!this.canExportTerm) return null;
                const term = this.terms.find((t) => t.id === this.statsTermId);
                return this.openPdfPreview(
                    this.pdfUrl({ termId: this.statsTermId }), 'Cahier de roulement — période',
                    `Cahier-de-roulement-${(term && term.label ? term.label : 'periode').replace(/\s+/g, '-')}.pdf`);
            },

            // ------------------------------------------------------------------------------- statistiques

            async loadStats() {
                if (!this.canStats) return;
                if (this.statsScope === 'class' && !this.classroomId) return;
                if (this.statsPeriod === 'term' && !this.statsTermId) return;

                const params = new URLSearchParams({ scope: this.statsScope });
                if (this.statsScope === 'class') params.set('classroomId', this.classroomId);
                if (this.statsPeriod === 'term') params.set('termId', this.statsTermId);
                else params.set('month', this.month);

                this.isLoadingStats = true;
                this.error = null;
                try {
                    this.stats = await window.api.get(`/roll-book/stats?${params.toString()}`);
                } catch (err) {
                    this.stats = null;
                    this.error = window.api.toMessage(err, 'Erreur lors du calcul des statistiques.');
                } finally {
                    this.isLoadingStats = false;
                }
            },

            showStats() {
                this.tab = 'stats';
                return this.loadStats();
            }
        }));
    });
})();
