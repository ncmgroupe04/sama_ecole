/**
 * Onglet « Coefficients » de l'écran Matières — Évolution N°4 (coefficients par série).
 *
 * Le Directeur règle ici le coefficient d'une matière pour UNE SÉRIE de lycée (L1, L2, S1, S2, TECH)
 * ou pour UNE CLASSE précise. Précédence appliquée par le serveur : classe › série › coefficient de la
 * matière. Ce fichier n'a AUCUNE règle métier : il affiche `effectiveCoefficient` et `source` tels que
 * le serveur les calcule (GET /api/v1/coefficients), il n'en recalcule jamais un lui-même.
 *
 * Écriture : Directeur seul (CoefficientsController). Le Secrétariat lit la grille, les champs lui
 * sont fermés — `canEdit` ne fait que masquer des boutons, le serveur reste seul juge (403).
 * Primaire et Maternelle n'ont pas de coefficient : leurs classes ne sont pas proposées.
 */
document.addEventListener('alpine:init', () => {
    // Cycles dont une classe peut porter une surcharge (arbitrage A8) : le serveur les refuse (422)
    // pour le primaire/maternelle, où le coefficient est neutralisé à 1.
    const CLASSROOM_CYCLES = ['College', 'Lycee'];

    const SOURCE_LABELS = { Subject: 'Matière', Series: 'Série', Classroom: 'Classe' };

    /** « 6,5 » (clavier français) → 6.5 ; vide ou illisible → null (jamais '' vers le serveur : 400 de model-binding). */
    function parseCoefficient(text) {
        const cleaned = String(text ?? '').trim().replace(',', '.');
        if (cleaned === '') return null;
        const value = Number(cleaned);
        return Number.isFinite(value) ? value : null;
    }

    /** Affichage d'un décimal à la française : 4 → « 4 », 6.5 → « 6,5 ». */
    function formatCoefficient(value) {
        return value === null || value === undefined ? '' : String(value).replace('.', ',');
    }

    Alpine.data('coefficientsView', () => ({
        // Portée : 'series' | 'classroom'
        scope: 'series',
        series: '',
        classroomId: '',

        catalog: [],
        classrooms: [],
        rows: [],
        yearLabel: '',
        yearHasGrades: false,
        previousYear: null, // { id, label } — source de « Reprendre l'année précédente »

        isLoading: false,
        error: null,
        notice: null,
        templateReport: null,
        isApplyingTemplate: false,
        isCarryingOver: false,

        get canEdit() {
            return window.auth.role === 'Directeur';
        },

        /** Le sélecteur « Classe » n'offre que le collège et le lycée (arbitrage A8). */
        get classroomChoices() {
            return this.classrooms
                .filter((c) => CLASSROOM_CYCLES.includes(c.cycle))
                .map((c) => ({ value: c.id, label: c.name }));
        },

        get seriesChoices() {
            return this.catalog.map((s) => ({ value: s.code, label: s.label }));
        },

        get isSeriesScope() {
            return this.scope === 'series';
        },

        /** « Appliquer le modèle » n'existe que pour une série (un modèle national décrit une série). */
        get canApplyTemplate() {
            return this.canEdit && this.isSeriesScope && !!this.series && !this.isApplyingTemplate;
        },

        get canCarryOver() {
            return this.canEdit && !!this.previousYear && !this.isCarryingOver;
        },

        async init() {
            this.isLoading = true;
            try {
                // Trois lectures indépendantes ; une classe ou une année introuvable ne doit pas
                // empêcher la grille par série de s'afficher.
                const [catalog, classrooms, years] = await Promise.all([
                    window.api.get('/coefficients/catalog'),
                    window.api.get('/classrooms').catch(() => []),
                    window.api.get('/school-years').catch(() => [])
                ]);
                this.catalog = catalog || [];
                this.classrooms = classrooms || [];
                this.previousYear = this.findPreviousYear(years || []);
                if (!this.series && this.catalog.length > 0) this.series = this.catalog[0].code;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des séries.');
                this.isLoading = false;
                return;
            }
            await this.load();
        },

        /** L'année terminée la plus récente qui n'est pas l'année active : la source naturelle d'une reconduction. */
        findPreviousYear(years) {
            const others = years
                .filter((y) => !y.isActive)
                .sort((a, b) => String(b.endDate).localeCompare(String(a.endDate)));
            return others.length > 0 ? { id: others[0].id, label: others[0].label } : null;
        },

        setScope(scope) {
            if (this.scope === scope) return;
            this.scope = scope;
            this.notice = null;
            this.templateReport = null;
            this.rows = [];
            this.load();
        },

        async load() {
            this.error = null;
            const query = this.isSeriesScope
                ? (this.series ? `series=${encodeURIComponent(this.series)}` : '')
                : (this.classroomId ? `classroomId=${encodeURIComponent(this.classroomId)}` : '');

            // Rien à interroger tant que la série ou la classe n'est pas choisie : un GET sans portée
            // serait un 422 pour rien.
            if (!query) {
                this.rows = [];
                this.isLoading = false;
                return;
            }

            this.isLoading = true;
            try {
                const grid = await window.api.get(`/coefficients?${query}`);
                this.yearLabel = grid.schoolYearLabel;
                this.yearHasGrades = grid.yearHasGrades;
                this.rows = (grid.rows || []).map((row) => this.toRow(row));
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des coefficients.');
            } finally {
                this.isLoading = false;
            }
        },

        toRow(row) {
            return {
                subjectId: row.subjectId,
                name: row.subjectName,
                level: row.level,
                base: row.baseCoefficient,
                overrideId: row.overrideId,
                override: row.overrideCoefficient,
                rowVersion: row.rowVersion,
                inherited: row.inheritedSeriesCoefficient,
                effective: row.effectiveCoefficient,
                source: row.source,
                draft: formatCoefficient(row.overrideCoefficient),
                isSaving: false,
                error: null
            };
        },

        selectSeries(code) {
            this.series = code;
            this.notice = null;
            this.templateReport = null;
            this.load();
        },

        selectClassroom(id) {
            this.classroomId = id;
            this.notice = null;
            this.load();
        },

        formatCoefficient,

        sourceLabel(source) {
            return SOURCE_LABELS[source] || source;
        },

        /** Pastille d'origine : la valeur du calcul vient de la matière (neutre), de la série ou de la classe. */
        sourceClass(source) {
            if (source === 'Classroom') return 'bg-amber-50 text-amber-700 ring-amber-200';
            if (source === 'Series') return 'bg-primary-50 text-primary-700 ring-primary-200';
            return 'bg-slate-100 text-slate-600 ring-slate-200';
        },

        /** Le champ diffère de ce qui est enregistré : seul un vrai changement active « Enregistrer ». */
        isDirty(row) {
            return parseCoefficient(row.draft) !== (row.override ?? null);
        },

        canSave(row) {
            return this.canEdit && !row.isSaving && row.draft.trim() !== '' && this.isDirty(row);
        },

        // ------------------------------------------------------------ Écriture

        async save(row) {
            const coefficient = parseCoefficient(row.draft);
            if (coefficient === null) {
                row.error = 'Saisissez un coefficient (nombre).';
                return;
            }

            const payload = { subjectId: row.subjectId, coefficient };
            if (this.isSeriesScope) payload.series = this.series;
            else payload.classroomId = this.classroomId;
            // Correction d'une ligne existante : le jeton xmin lu avec elle (verrou optimiste, règle #5).
            if (row.overrideId) payload.rowVersion = row.rowVersion;

            row.isSaving = true;
            row.error = null;
            this.notice = null;
            try {
                await window.api.put('/coefficients', payload);
                await this.load();
                this.notice = `Coefficient de « ${row.name} » enregistré.`;
            } catch (err) {
                if (this.isConflict(err)) {
                    await this.handleConflict();
                } else {
                    row.error = window.api.toMessage(err, "Erreur lors de l'enregistrement.");
                }
            } finally {
                row.isSaving = false;
            }
        },

        /** « Rétablir » : supprime la surcharge, la valeur héritée (série, puis matière) reprend la main. */
        async restore(row) {
            if (!this.canEdit || !row.overrideId) return;

            row.isSaving = true;
            row.error = null;
            this.notice = null;
            try {
                await window.api.delete(`/coefficients/${row.overrideId}?rowVersion=${row.rowVersion}`);
                await this.load();
                this.notice = `Coefficient de « ${row.name} » rétabli.`;
            } catch (err) {
                if (this.isConflict(err)) {
                    await this.handleConflict();
                } else {
                    row.error = window.api.toMessage(err, 'Erreur lors du rétablissement.');
                }
            } finally {
                row.isSaving = false;
            }
        },

        isConflict(err) {
            return !!err && (err.status === 409 || err.code === 'CONCURRENCY_CONFLICT');
        },

        /** Jamais un écrasement silencieux : on recharge l'état réel et on le dit. */
        async handleConflict() {
            await this.load();
            this.error = 'Ce coefficient a été modifié entre-temps par un autre utilisateur. La grille a été rechargée — vérifiez les valeurs puis réessayez.';
        },

        // ------------------------------------------------------------ Modèle national et reconduction

        /**
         * « Appliquer le modèle » : le serveur matérialise le modèle national de la série en surcharges de
         * série (visibles et modifiables ici). Sans `overwrite`, les valeurs déjà posées sont conservées.
         */
        async applyTemplate(overwrite = false) {
            if (!this.canApplyTemplate) return;

            this.isApplyingTemplate = true;
            this.error = null;
            this.notice = null;
            try {
                const report = await window.api.post('/coefficients/apply-template', { series: this.series, overwrite });
                this.templateReport = {
                    applied: report.applied,
                    updated: report.updated,
                    skippedExisting: report.skippedExisting,
                    unmatched: report.unmatchedTemplateLines || [],
                    uncovered: report.uncoveredSubjects || []
                };
                await this.load();
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'application du modèle.");
            } finally {
                this.isApplyingTemplate = false;
            }
        },

        /** « Reprendre l'année précédente » : recopie les surcharges de l'année passée sans rien écraser. */
        async carryOver() {
            if (!this.canCarryOver) return;

            this.isCarryingOver = true;
            this.error = null;
            this.notice = null;
            try {
                const result = await window.api.post('/coefficients/carry-over', { fromSchoolYearId: this.previousYear.id });
                await this.load();
                this.notice = `Année ${this.previousYear.label} reprise : ${result.copied} coefficient(s) copié(s), ${result.skipped} déjà présent(s) ou sans objet.`;
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de la reprise de l'année précédente.");
            } finally {
                this.isCarryingOver = false;
            }
        }
    }));
});
