/**
 * Volumes horaires et conformité des emplois du temps (Évolution N°7).
 *
 * - `hourNormsPanel` (écran /programmes, onglet « Volumes horaires ») : pour un niveau et, au lycée, une série, le
 *   volume hebdomadaire de chaque matière — grille de référence, réglage de l'école, volume effectif
 *   (GET /api/v1/hour-volumes/norms). Le Directeur règle ou rétablit un volume (PUT / DELETE .../norms).
 * - `timetableCompliance` (écran Enseignants, onglet Emploi du temps, vue « Par classe ») : heures planifiées de la
 *   classe contre sa référence, matière par matière, et chevauchements d'enseignant, de salle ou de classe
 *   (GET /api/v1/hour-volumes/compliance). Imbriqué dans `teachersView`, il se recharge quand la classe choisie ou
 *   la grille de créneaux change (`sync`, appelé par x-effect).
 *
 * Aucune règle métier ici : statuts, écarts et chevauchements viennent du serveur. `canEdit` ne fait que masquer des
 * boutons, le serveur reste seul juge (403).
 */
document.addEventListener('alpine:init', () => {
    const GRADE_LEVELS = [
        'CI', 'CP', 'CE1', 'CE2', 'CM1', 'CM2',
        'Sixième', 'Cinquième', 'Quatrième', 'Troisième',
        'Seconde', 'Première', 'Terminale'
    ];
    const LYCEE_GRADES = ['Seconde', 'Première', 'Terminale'];
    const SERIES = ['L1A', 'L1B', "L'1", 'L2', 'S1', 'S2', 'S3', 'S4', 'S5', 'STEG', 'T1', 'T2', 'STIDD', 'LA', 'S1A', 'S2A'];

    const DAY_LABELS = {
        Monday: 'Lundi', Tuesday: 'Mardi', Wednesday: 'Mercredi', Thursday: 'Jeudi',
        Friday: 'Vendredi', Saturday: 'Samedi', Sunday: 'Dimanche'
    };

    const STATUS = {
        Compliant: { label: 'Conforme', css: 'bg-emerald-50 text-emerald-700 ring-emerald-200' },
        Under: { label: 'Sous le volume', css: 'bg-amber-50 text-amber-700 ring-amber-200' },
        Over: { label: 'Au-dessus', css: 'bg-red-50 text-red-700 ring-red-200' },
        NoReference: { label: 'Sans référence', css: 'bg-slate-100 text-slate-600 ring-slate-200' }
    };

    const CONFLICT_LABELS = { Teacher: 'Enseignant', Room: 'Salle', Classroom: 'Classe' };

    /** « 2,5 » (clavier français) → 2.5 ; vide → null ; illisible → NaN. */
    function parseHours(text) {
        const cleaned = String(text ?? '').trim().replace(',', '.');
        if (cleaned === '') return null;
        return Number(cleaned);
    }

    /** 2.5 → « 2 h 30 » ; 3 → « 3 h » ; null → « — ». */
    function formatHours(value) {
        if (value === null || value === undefined) return '—';
        const negative = value < 0;
        const minutes = Math.round(Math.abs(value) * 60);
        const h = Math.floor(minutes / 60);
        const m = minutes % 60;
        return `${negative ? '−' : ''}${h} h${m ? ` ${String(m).padStart(2, '0')}` : ''}`;
    }

    function formatDifference(value) {
        if (value === null || value === undefined) return '—';
        if (Math.abs(value) < 1 / 60) return '0';
        return value > 0 ? `+${formatHours(value)}` : formatHours(value);
    }

    const display = {
        formatHours,
        formatDifference,
        statusLabel(status) { return (STATUS[status] || STATUS.NoReference).label; },
        statusClass(status) { return (STATUS[status] || STATUS.NoReference).css; },
        dayLabel(day) { return DAY_LABELS[day] || day; },
        conflictLabel(kind) { return CONFLICT_LABELS[kind] || kind; },
        time(value) { return (value || '').substring(0, 5); }
    };

    // ======================================================================== Volumes de référence
    Alpine.data('hourNormsPanel', () => ({
        ...display,
        gradeLevel: '',
        series: '',
        data: null,
        rows: [],
        isLoading: false,
        error: null,
        notice: null,

        get canEdit() {
            return window.auth.role === 'Directeur';
        },

        get gradeOptions() {
            return GRADE_LEVELS.map((g) => ({ value: g, label: g }));
        },

        get isLycee() {
            return LYCEE_GRADES.includes(this.gradeLevel);
        },

        get seriesOptions() {
            return [{ value: '', label: 'Toutes séries' }].concat(SERIES.map((s) => ({ value: s, label: s })));
        },

        selectGrade() {
            if (!this.isLycee) this.series = '';
            this.load();
        },

        async load() {
            this.error = null;
            this.notice = null;
            if (!this.gradeLevel) {
                this.data = null;
                this.rows = [];
                return;
            }

            this.isLoading = true;
            try {
                const params = new URLSearchParams({ gradeLevel: this.gradeLevel });
                if (this.series) params.set('series', this.series);
                this.data = await window.api.get(`/hour-volumes/norms?${params.toString()}`);
                this.rows = (this.data.rows || []).map((row) => ({
                    ...row,
                    draft: row.schoolHours === null || row.schoolHours === undefined ? '' : String(row.schoolHours).replace('.', ','),
                    isSaving: false,
                    error: null
                }));
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des volumes horaires.');
            } finally {
                this.isLoading = false;
            }
        },

        /** Le volume saisi diffère-t-il du réglage enregistré ? (vide = pas de réglage propre) */
        isDirty(row) {
            const value = parseHours(row.draft);
            return value !== (row.schoolHours ?? null);
        },

        async run(row, action, success) {
            row.isSaving = true;
            row.error = null;
            this.notice = null;
            try {
                await action();
                await this.load();
                this.notice = success;
            } catch (err) {
                if (err && (err.status === 409 || err.code === 'CONCURRENCY_CONFLICT')) {
                    await this.load();
                    this.error = 'Ce volume a été modifié entre-temps par un autre utilisateur. La liste a été rechargée — vérifiez puis réessayez.';
                } else {
                    row.error = window.api.toMessage(err, "Erreur lors de l'enregistrement.");
                }
            } finally {
                row.isSaving = false;
            }
        },

        save(row) {
            const hours = parseHours(row.draft);
            if (hours === null) {
                this.reset(row);
                return;
            }
            if (Number.isNaN(hours)) {
                row.error = "Volume illisible : saisissez un nombre d'heures (ex. 4 ou 2,5).";
                return;
            }
            const payload = {
                gradeLevel: this.gradeLevel,
                series: this.series || null,
                subjectId: row.subjectId,
                weeklyHours: hours,
                rowVersion: row.overrideId ? row.rowVersion : null
            };
            this.run(row, () => window.api.put('/hour-volumes/norms', payload), `Volume de « ${row.subjectName} » enregistré.`);
        },

        /** « Revenir à la référence » : retire le réglage propre à l'école. */
        reset(row) {
            if (!row.overrideId) {
                row.draft = '';
                return;
            }
            this.run(row,
                () => window.api.delete(`/hour-volumes/norms/${row.overrideId}?rowVersion=${row.rowVersion}`),
                `« ${row.subjectName} » revient au volume de référence.`);
        }
    }));

    // ======================================================================== Conformité d'une classe
    Alpine.data('timetableCompliance', () => ({
        ...display,
        classroomId: '',
        slotsRef: null,
        compliance: null,
        isLoading: false,
        error: null,
        showConflicts: false,

        get visible() {
            return ['Directeur', 'Secretariat'].includes(window.auth.role);
        },

        get classCompliance() {
            return (this.compliance && this.compliance.classes && this.compliance.classes[0]) || null;
        },

        get conflicts() {
            return (this.compliance && this.compliance.conflicts) || [];
        },

        get issues() {
            return this.classCompliance
                ? this.classCompliance.subjects.filter((s) => s.status === 'Under' || s.status === 'Over').length
                : 0;
        },

        /**
         * Appelé par x-effect avec la classe choisie et la grille de créneaux du parent : recharge quand la classe
         * change, ou quand la grille a été rechargée (créneau ajouté, déplacé, supprimé).
         */
        sync(classroomId, slots) {
            if (!this.visible) return;
            if (classroomId === this.classroomId && slots === this.slotsRef) return;
            this.classroomId = classroomId || '';
            this.slotsRef = slots;
            this.load();
        },

        async load() {
            this.error = null;
            if (!this.classroomId) {
                this.compliance = null;
                return;
            }

            const requested = this.classroomId;
            this.isLoading = true;
            try {
                const data = await window.api.get(`/hour-volumes/compliance?classroomId=${encodeURIComponent(requested)}`);
                if (requested === this.classroomId) this.compliance = data;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du contrôle de conformité de l\'emploi du temps.');
            } finally {
                this.isLoading = false;
            }
        },

        slotLabel(slot) {
            return `${slot.classroomName} · ${slot.subjectName} · ${slot.teacherName} (${this.time(slot.startTime)}–${this.time(slot.endTime)})`;
        }
    }));
});
