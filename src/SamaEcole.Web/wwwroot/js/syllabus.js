/**
 * Programmes nationaux et avancement (Évolution N°7) — écran /programmes.
 *
 * Deux onglets :
 * - « Avancement » : pour chaque classe et chaque matière dont le programme est saisi, la part des chapitres pointés
 *   au cahier de texte de l'année active, puis les moyennes par matière et par enseignant
 *   (GET /api/v1/syllabus/coverage, Directeur et Secrétariat) ;
 * - « Référentiel » : les chapitres d'une matière pour un niveau (GET /api/v1/syllabus/units), que le Directeur
 *   importe depuis la trame nationale quand elle existe, complète (un chapitre par ligne), corrige ou archive ;
 * - « Volumes horaires » : composant `hourNormsPanel` (hour-volumes.js). `?tab=hours` ouvre directement cet onglet
 *   (lien depuis le contrôle de conformité de l'emploi du temps).
 *
 * Ce fichier n'a AUCUNE règle métier : pourcentages, moyennes et niveau d'une classe viennent du serveur. `canEdit`
 * ne fait que masquer des boutons, le serveur reste seul juge (403).
 */
document.addEventListener('alpine:init', () => {
    // Niveaux du système éducatif sénégalais — mêmes libellés que le serveur (AgeNormTemplates).
    const GRADE_LEVELS = [
        'CI', 'CP', 'CE1', 'CE2', 'CM1', 'CM2',
        'Sixième', 'Cinquième', 'Quatrième', 'Troisième',
        'Seconde', 'Première', 'Terminale'
    ];

    /** « 2,5 » (clavier français) → 2.5 ; vide → null ; illisible → NaN (refusé à l'enregistrement). */
    function parseHours(text) {
        const cleaned = String(text ?? '').trim().replace(',', '.');
        if (cleaned === '') return null;
        return Number(cleaned);
    }

    function formatNumber(value) {
        return value === null || value === undefined ? '' : String(value).replace('.', ',');
    }

    function emptyAddForm() {
        return { section: '', titlesText: '' };
    }

    Alpine.data('syllabusView', () => ({
        tab: 'coverage',
        error: null,
        notice: null,

        // ---------------------------------------------------------------- Avancement
        coverage: null,
        isLoadingCoverage: false,
        coverageFilter: '',

        // ---------------------------------------------------------------- Référentiel
        subjects: [],
        subjectId: '',
        gradeLevel: '',
        programme: null,
        rows: [],
        isLoadingUnits: false,
        isImporting: false,
        addForm: emptyAddForm(),
        isAdding: false,
        addError: null,

        get canEdit() {
            return window.auth.role === 'Directeur';
        },

        get gradeOptions() {
            return GRADE_LEVELS.map((g) => ({ value: g, label: g }));
        },

        /** Matières de l'établissement ; les domaines APC (matières parentes) ne portent pas de programme propre. */
        get subjectOptions() {
            const domainIds = new Set(this.subjects.filter((s) => s.parentSubjectId).map((s) => s.parentSubjectId));
            return this.subjects
                .filter((s) => !domainIds.has(s.id))
                .map((s) => ({ value: s.id, label: s.level ? `${s.name} (${s.level})` : s.name }));
        },

        get classroomFilterOptions() {
            const names = [...new Set(((this.coverage && this.coverage.rows) || []).map((r) => r.classroomName))];
            return [{ value: '', label: 'Toutes les classes' }].concat(names.map((n) => ({ value: n, label: n })));
        },

        get coverageRows() {
            const rows = (this.coverage && this.coverage.rows) || [];
            return this.coverageFilter ? rows.filter((r) => r.classroomName === this.coverageFilter) : rows;
        },

        get canAdd() {
            return this.canEdit && !this.isAdding && this.titles().length > 0;
        },

        async init() {
            const requestedTab = new URLSearchParams(window.location.search || '').get('tab');
            if (['coverage', 'units', 'hours'].includes(requestedTab)) this.tab = requestedTab;
            await Promise.all([this.loadCoverage(), this.loadSubjects()]);
        },

        async loadSubjects() {
            try {
                this.subjects = (await window.api.get('/subjects')) || [];
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des matières.');
            }
        },

        async loadCoverage() {
            this.isLoadingCoverage = true;
            try {
                this.coverage = await window.api.get('/syllabus/coverage');
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors du chargement de l'avancement des programmes.");
            } finally {
                this.isLoadingCoverage = false;
            }
        },

        // ---------------------------------------------------------------- Affichage

        formatNumber,

        percentLabel(percent) {
            return percent === null || percent === undefined ? '—' : `${formatNumber(percent)} %`;
        },

        /** Barre de progression : vert au-delà des deux tiers, ambre au-delà du tiers, rouge en dessous. */
        percentClass(percent) {
            if (percent === null || percent === undefined) return 'bg-slate-200';
            if (percent >= 66) return 'bg-emerald-500';
            if (percent >= 33) return 'bg-amber-400';
            return 'bg-red-400';
        },

        percentWidth(percent) {
            const value = Math.max(0, Math.min(100, Number(percent) || 0));
            return `width: ${value}%`;
        },

        formatDate(isoDate) {
            if (!isoDate) return '—';
            const [year, month, day] = isoDate.split('-');
            return `${day}/${month}/${year}`;
        },

        /** Ouvre le référentiel d'une ligne du tableau d'avancement. */
        openProgramme(row) {
            this.subjectId = row.subjectId;
            this.gradeLevel = row.gradeLevel;
            this.tab = 'units';
            this.loadUnits();
        },

        // ---------------------------------------------------------------- Référentiel : lecture

        async loadUnits() {
            this.error = null;
            this.notice = null;
            if (!this.subjectId || !this.gradeLevel) {
                this.programme = null;
                this.rows = [];
                return;
            }

            this.isLoadingUnits = true;
            try {
                const params = new URLSearchParams({ subjectId: this.subjectId, gradeLevel: this.gradeLevel });
                this.programme = await window.api.get(`/syllabus/units?${params.toString()}`);
                this.rows = (this.programme.units || []).map((unit) => ({
                    ...unit,
                    draftTitle: unit.title,
                    draftSection: unit.section || '',
                    draftHours: formatNumber(unit.plannedHours),
                    isSaving: false,
                    confirmDelete: false,
                    error: null
                }));
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement du programme.');
            } finally {
                this.isLoadingUnits = false;
            }
        },

        isDirty(row) {
            return row.draftTitle.trim() !== row.title
                || row.draftSection.trim() !== (row.section || '')
                || parseHours(row.draftHours) !== (row.plannedHours ?? null);
        },

        // ---------------------------------------------------------------- Référentiel : écriture (Directeur)

        isConflict(err) {
            return !!err && (err.status === 409 || err.code === 'CONCURRENCY_CONFLICT');
        },

        async run(row, action, success) {
            row.isSaving = true;
            row.error = null;
            this.notice = null;
            try {
                await action();
                await this.loadUnits();
                this.notice = success;
            } catch (err) {
                if (this.isConflict(err)) {
                    await this.loadUnits();
                    this.error = 'Ce chapitre a été modifié entre-temps par un autre utilisateur. Le programme a été rechargé — vérifiez puis réessayez.';
                } else {
                    row.error = window.api.toMessage(err, "Erreur lors de l'enregistrement.");
                }
            } finally {
                row.isSaving = false;
            }
        },

        saveUnit(row) {
            const plannedHours = parseHours(row.draftHours);
            if (Number.isNaN(plannedHours)) {
                row.error = 'Volume horaire illisible : saisissez un nombre d\'heures (ex. 6 ou 2,5).';
                return;
            }
            if (!row.draftTitle.trim()) {
                row.error = "Saisissez l'intitulé du chapitre.";
                return;
            }
            this.run(row, () => window.api.put(`/syllabus/units/${row.id}`, {
                title: row.draftTitle.trim(),
                section: row.draftSection.trim() || null,
                order: row.order,
                plannedHours,
                rowVersion: row.rowVersion
            }), 'Chapitre enregistré.');
        },

        /** Monte ou descend un chapitre d'un rang : échange son ordre avec celui de son voisin. */
        move(row, delta) {
            const index = this.rows.indexOf(row);
            const other = this.rows[index + delta];
            if (!other) return;
            this.run(row, async () => {
                await window.api.put(`/syllabus/units/${row.id}`, {
                    title: row.title, section: row.section, order: other.order, plannedHours: row.plannedHours, rowVersion: row.rowVersion
                });
                await window.api.put(`/syllabus/units/${other.id}`, {
                    title: other.title, section: other.section, order: row.order, plannedHours: other.plannedHours, rowVersion: other.rowVersion
                });
            }, 'Ordre du programme mis à jour.');
        },

        /** Archive un chapitre (suppression logique) — confirmé en deux temps. */
        archive(row) {
            if (!row.confirmDelete) {
                row.confirmDelete = true;
                return;
            }
            this.run(row,
                () => window.api.delete(`/syllabus/units/${row.id}?rowVersion=${row.rowVersion}`),
                `« ${row.title} » retiré du programme.`);
        },

        /** Un chapitre par ligne non vide. */
        titles() {
            return this.addForm.titlesText.split('\n').map((t) => t.trim()).filter((t) => t.length > 0);
        },

        async submitAdd() {
            if (!this.canAdd) return;
            this.isAdding = true;
            this.addError = null;
            this.notice = null;
            try {
                const result = await window.api.post('/syllabus/units', {
                    subjectId: this.subjectId,
                    gradeLevel: this.gradeLevel,
                    section: this.addForm.section.trim() || null,
                    titles: this.titles()
                });
                this.addForm = emptyAddForm();
                await this.loadUnits();
                this.notice = `${result.added} chapitre(s) ajouté(s) au programme.`;
            } catch (err) {
                this.addError = window.api.toMessage(err, "Erreur lors de l'ajout des chapitres.");
            } finally {
                this.isAdding = false;
            }
        },

        async importTemplate() {
            this.isImporting = true;
            this.error = null;
            this.notice = null;
            try {
                const result = await window.api.post('/syllabus/import-template', {
                    subjectId: this.subjectId, gradeLevel: this.gradeLevel
                });
                await this.loadUnits();
                this.notice = result.added > 0
                    ? `Trame nationale importée : ${result.added} chapitre(s) ajouté(s).`
                    : 'La trame nationale est déjà entièrement importée.';
            } catch (err) {
                this.error = window.api.toMessage(err, "Erreur lors de l'import de la trame nationale.");
            } finally {
                this.isImporting = false;
            }
        }
    }));
});
