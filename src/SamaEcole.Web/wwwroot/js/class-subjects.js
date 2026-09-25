/**
 * Onglet « Matières par classe » de l'écran Matières — Évolution N°6 (séries du Baccalauréat, options).
 *
 * Le programme d'UNE classe : les matières injectées depuis le modèle national de sa série, leur coefficient
 * effectif pour l'année active, leur groupe d'options (badge « Option obligatoire »), et la valeur officielle.
 * Le Directeur y modifie un coefficient, ajoute une matière propre à l'établissement, désactive une matière,
 * ou réinitialise la classe aux coefficients officiels du Sénégal.
 *
 * Ce fichier n'a AUCUNE règle métier : le coefficient effectif et son origine viennent du serveur
 * (GET /api/v1/class-subjects). Un coefficient s'écrit par PUT /api/v1/coefficients (portée classe), la même
 * écriture que l'onglet Coefficients. `canEdit` ne fait que masquer des boutons, le serveur reste seul juge (403).
 */
document.addEventListener('alpine:init', () => {
    // Le programme par classe ne concerne que le secondaire : au primaire, le coefficient vaut toujours 1.
    const CLASSROOM_CYCLES = ['College', 'Lycee'];

    const SOURCE_LABELS = { Subject: 'Matière', Series: 'Série', Classroom: 'Classe' };

    /** « 6,5 » (clavier français) → 6.5 ; vide ou illisible → null. */
    function parseCoefficient(text) {
        const cleaned = String(text ?? '').trim().replace(',', '.');
        if (cleaned === '') return null;
        const value = Number(cleaned);
        return Number.isFinite(value) ? value : null;
    }

    function formatCoefficient(value) {
        return value === null || value === undefined ? '' : String(value).replace('.', ',');
    }

    function emptyAddForm() {
        return { mode: 'existing', subjectId: '', newSubjectName: '', coefficient: '', optionGroup: '' };
    }

    Alpine.data('classSubjectsView', () => ({
        classrooms: [],
        subjects: [],
        classroomId: '',
        data: null,
        rows: [],

        isLoading: false,
        error: null,
        notice: null,
        confirmReset: false,
        isResetting: false,
        isAssigning: false,
        isAddOpen: false,
        isAdding: false,
        addForm: emptyAddForm(),
        addError: null,

        get canEdit() {
            return window.auth.role === 'Directeur';
        },

        /** Affecter les options par défaut : le Secrétariat, qui gère les inscriptions, le peut aussi. */
        get canAssign() {
            return ['Directeur', 'Secretariat'].includes(window.auth.role);
        },

        get classroomChoices() {
            return this.classrooms
                .filter((c) => CLASSROOM_CYCLES.includes(c.cycle))
                .map((c) => ({ value: c.id, label: c.series ? `${c.name} · ${c.series}` : c.name }));
        },

        /** Matières de l'établissement pas encore au programme de la classe (hors primaire et domaines APC). */
        get addableSubjects() {
            const taken = new Set(this.rows.map((r) => r.subjectId));
            const domainIds = new Set(this.subjects.filter((s) => s.parentSubjectId).map((s) => s.parentSubjectId));
            return this.subjects
                .filter((s) => !taken.has(s.id) && !s.parentSubjectId && !domainIds.has(s.id))
                .map((s) => ({ value: s.id, label: `${s.name} (${s.level})` }));
        },

        get groups() {
            return (this.data && this.data.optionGroups) || [];
        },

        get studentsWithoutOption() {
            return this.groups.reduce((sum, g) => sum + g.studentsWithoutChoice, 0);
        },

        get canReset() {
            return this.canEdit && !!this.data && this.data.hasTemplate && !this.isResetting;
        },

        async init() {
            try {
                const [classrooms, subjects] = await Promise.all([
                    window.api.get('/classrooms'),
                    window.api.get('/subjects').catch(() => [])
                ]);
                this.classrooms = classrooms || [];
                this.subjects = subjects || [];

                // Première classe avec série : c'est elle qui a un programme à montrer.
                const withSeries = this.classrooms.find((c) => c.series && CLASSROOM_CYCLES.includes(c.cycle));
                if (withSeries) {
                    this.classroomId = withSeries.id;
                    await this.load();
                }
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des classes.');
            }
        },

        selectClassroom(id) {
            this.classroomId = id;
            this.notice = null;
            this.confirmReset = false;
            this.isAddOpen = false;
            this.load();
        },

        async load() {
            this.error = null;
            if (!this.classroomId) {
                this.data = null;
                this.rows = [];
                return;
            }

            this.isLoading = true;
            try {
                this.data = await window.api.get(`/class-subjects?classroomId=${encodeURIComponent(this.classroomId)}`);
                this.rows = (this.data.rows || []).map((row) => ({
                    ...row,
                    draft: formatCoefficient(row.effectiveCoefficient),
                    groupDraft: row.optionGroup || '',
                    isSaving: false,
                    error: null
                }));
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du programme de la classe.');
            } finally {
                this.isLoading = false;
            }
        },

        formatCoefficient,

        sourceLabel(source) {
            return SOURCE_LABELS[source] || source;
        },

        sourceClass(source) {
            if (source === 'Classroom') return 'bg-amber-50 text-amber-700 ring-amber-200';
            if (source === 'Series') return 'bg-primary-50 text-primary-700 ring-primary-200';
            return 'bg-slate-100 text-slate-600 ring-slate-200';
        },

        /** Le coefficient effectif s'écarte-t-il de la valeur officielle de la série ? */
        differsFromOfficial(row) {
            return row.officialCoefficient !== null && row.officialCoefficient !== undefined
                && Number(row.officialCoefficient) !== Number(row.effectiveCoefficient);
        },

        defaultOptionName(group) {
            const row = this.rows.find((r) => r.id === group.defaultClassSubjectId);
            return row ? row.subjectName : '—';
        },

        canSaveCoefficient(row) {
            const value = parseCoefficient(row.draft);
            return this.canEdit && !row.isSaving && value !== null && value !== Number(row.effectiveCoefficient);
        },

        isConflict(err) {
            return !!err && (err.status === 409 || err.code === 'CONCURRENCY_CONFLICT');
        },

        async handleConflict() {
            await this.load();
            this.error = 'Cette ligne a été modifiée entre-temps par un autre utilisateur. Le programme a été rechargé — vérifiez puis réessayez.';
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
                if (this.isConflict(err)) await this.handleConflict();
                else row.error = window.api.toMessage(err, "Erreur lors de l'enregistrement.");
            } finally {
                row.isSaving = false;
            }
        },

        // ------------------------------------------------------------ Écriture (Directeur)

        /** Coefficient de la classe : la surcharge de classe de l'année active (moteur de l'Évolution N°4). */
        saveCoefficient(row) {
            const coefficient = parseCoefficient(row.draft);
            if (coefficient === null) {
                row.error = 'Saisissez un coefficient (nombre).';
                return;
            }
            const payload = { subjectId: row.subjectId, classroomId: this.classroomId, coefficient };
            if (row.overrideId) payload.rowVersion = row.overrideRowVersion;
            this.run(row, () => window.api.put('/coefficients', payload), `Coefficient de « ${row.subjectName} » enregistré.`);
        },

        /** « Rétablir » : retire le coefficient propre à la classe — la valeur de la série ou de la matière reprend la main. */
        restoreCoefficient(row) {
            if (!row.overrideId) return;
            this.run(row,
                () => window.api.delete(`/coefficients/${row.overrideId}?rowVersion=${row.overrideRowVersion}`),
                `Coefficient de « ${row.subjectName} » rétabli.`);
        },

        toggleActive(row) {
            this.updateRow(row, !row.isActive, row.optionGroup,
                row.isActive ? `« ${row.subjectName} » désactivée pour la classe.` : `« ${row.subjectName} » réactivée.`);
        },

        saveGroup(row) {
            const group = row.groupDraft.trim();
            if (group === (row.optionGroup || '')) return;
            this.updateRow(row, row.isActive, group || null,
                group ? `« ${row.subjectName} » est une option du groupe « ${group} ».` : `« ${row.subjectName} » est suivie par toute la classe.`);
        },

        updateRow(row, isActive, optionGroup, success) {
            this.run(row,
                () => window.api.put(`/class-subjects/${row.id}`, { isActive, optionGroup, rowVersion: row.rowVersion }),
                success);
        },

        openAdd() {
            this.addForm = emptyAddForm();
            this.addError = null;
            this.isAddOpen = true;
        },

        async submitAdd() {
            const coefficient = parseCoefficient(this.addForm.coefficient);
            const payload = {
                classroomId: this.classroomId,
                coefficient,
                optionGroup: this.addForm.optionGroup.trim() || null
            };
            if (this.addForm.mode === 'existing') payload.subjectId = this.addForm.subjectId || null;
            else payload.newSubjectName = this.addForm.newSubjectName.trim() || null;

            this.isAdding = true;
            this.addError = null;
            try {
                await window.api.post('/class-subjects', payload);
                this.isAddOpen = false;
                this.subjects = await window.api.get('/subjects').catch(() => this.subjects);
                await this.load();
                this.notice = 'Matière ajoutée au programme de la classe.';
            } catch (err) {
                this.addError = window.api.toMessage(err, "Erreur lors de l'ajout de la matière.");
            } finally {
                this.isAdding = false;
            }
        },

        /** « Réinitialiser aux coefficients officiels du Sénégal » — confirmé en deux temps, jamais au premier clic. */
        async reset() {
            if (!this.canReset) return;
            this.isResetting = true;
            this.error = null;
            this.notice = null;
            try {
                const report = await window.api.post('/class-subjects/reset', { classroomId: this.classroomId });
                this.confirmReset = false;
                await this.load();
                this.notice = `Coefficients officiels rétablis : ${report.coefficientsSet + report.coefficientsCleared} coefficient(s) corrigé(s), `
                    + `${report.subjectsAdded + report.subjectsRestored} matière(s) ajoutée(s) ou réactivée(s).`;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors de la réinitialisation.');
            } finally {
                this.isResetting = false;
            }
        },

        async assignDefaults() {
            this.isAssigning = true;
            this.error = null;
            this.notice = null;
            try {
                const result = await window.api.post('/class-subjects/assign-default-options', { classroomId: this.classroomId });
                await this.load();
                this.notice = `${result.optionsAssigned} option(s) par défaut attribuée(s) à ${result.studentsUpdated} élève(s).`;
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'affectation des options.");
            } finally {
                this.isAssigning = false;
            }
        }
    }));
});
