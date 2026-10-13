/**
 * Espace de l'Oustaz (/halqa) — tablette : sa Halqa, la grille des 60 Hizb de chaque élève regroupés par Juz, et le
 * tiroir de saisie des quarts. Écran en arabe, de droite à gauche (dir="rtl" posé sur la vue).
 *
 * Consomme GET /api/v1/internat/my-halqa (la Halqa de l'Oustaz connecté, résolue côté serveur depuis son compte —
 * aucun identifiant n'est passé), GET /api/v1/internat/students/{id}/hizb-progress (la grille) et
 * PUT /api/v1/internat/students/{id}/hizb-progress (un Hizb à la fois), plus GET .../students/{id}/hizb-report/pdf pour
 * le bulletin coranique de l'élève ouvert (bouton « كشف الحفظ », modale PDF commune). La portée (un Oustaz ne voit et n'écrit que
 * SA Halqa), l'état d'un Hizb (déduit des quarts) et la date d'évaluation sont décidés par le SERVEUR : ce fichier
 * n'affiche que ce qu'il renvoie, et n'envoie ni état ni date.
 *
 * CONFLIT D'ÉCRITURE : chaque case de la grille porte le jeton xmin de sa ligne (`rowVersion`, null tant que le Hizb
 * n'a jamais été saisi), renvoyé tel quel à l'enregistrement. Un 409 signifie qu'une autre personne (ou le même Oustaz
 * sur une autre tablette) a modifié ce Hizb entre-temps : on recharge la grille au lieu d'écraser, et on le dit.
 *
 * La logique pure (regroupement par Juz, libellés, nom d'affichage, résumé) vit dans `window.halqaLogic`, testée sans
 * navigateur (tests/js/halqa.test.mjs).
 */
(function () {
    'use strict';

    const HIZB_COUNT = 60;
    const TOTAL_QUARTERS = HIZB_COUNT * 4;

    /** Libellés de l'écran — centralisés ici pour qu'une traduction ou une correction ne touche qu'un endroit. */
    const LABELS = {
        quarters: {
            0: 'لم يبدأ',
            1: 'الربع الأول',
            2: 'النصف',
            3: 'ثلاثة أرباع',
            4: 'الحزب كامل'
        },
        loadError: 'تعذّر تحميل الحلقة.',
        historyError: 'تعذّر تحميل التطور.',
        historyChart: 'منحنى تطور الحفظ',
        periods: [
            { key: '3m', label: '3 أشهر' },
            { key: '6m', label: '6 أشهر' },
            { key: '12m', label: '12 شهرًا' },
            { key: 'all', label: 'الكل' }
        ],
        hizb: 'الحزب',
        from: 'من',
        to: 'إلى',
        gridError: 'تعذّر تحميل جدول الأحزاب.',
        saveError: 'تعذّر حفظ التقييم.',
        conflict: 'عُدّل هذا الحزب من طرف آخر قبل حفظك. تم تحديث الجدول، يمكنك إعادة المحاولة.'
    };

    /**
     * Nom à afficher : l'arabe s'il est renseigné, sinon le nom français. Jamais une chaîne vide ni un blanc : un
     * `fullNameAr` « » ou « ، » de pure espace retombe sur le nom français (même principe que le reste de l'app, où
     * l'arabe est saisi librement et peut être absent).
     */
    function pickName(arabic, french) {
        const ar = typeof arabic === 'string' ? arabic.trim() : '';
        return ar !== '' ? ar : (french || '');
    }

    /** Numéro de Juz (1 à 30) d'un Hizb : chaque Juz contient deux Hizb consécutifs (1-2, 3-4, … 59-60). */
    function juzOfHizb(hizbNumber) {
        return Math.ceil(hizbNumber / 2);
    }

    /** Regroupe la grille des 60 cases en 30 Juz de deux Hizb, dans l'ordre. */
    function groupByJuz(cells) {
        const groups = [];
        (cells || []).forEach((cell) => {
            const juz = juzOfHizb(cell.hizbNumber);
            let group = groups[groups.length - 1];
            if (!group || group.juz !== juz) {
                group = { juz, hizbs: [] };
                groups.push(group);
            }
            group.hizbs.push(cell);
        });
        return groups;
    }

    function quarterLabel(quarters) {
        return LABELS.quarters[quarters] ?? '';
    }

    /**
     * Résumé de la grille, recalculé avec la MÊME règle que le serveur (HizbRules.ProgressPercent : quarts acquis sur
     * 240, une décimale, arrondi loin de zéro) pour mettre à jour l'en-tête après une saisie sans second aller-retour.
     */
    function summarize(cells) {
        const list = cells || [];
        const quarters = list.reduce((sum, c) => sum + (c.completedQuarters || 0), 0);
        return {
            completedHizbs: list.filter((c) => c.state === 'Completed').length,
            inProgressHizbs: list.filter((c) => c.state === 'InProgress').length,
            completedQuarters: quarters,
            totalQuarters: TOTAL_QUARTERS,
            progressPercent: Math.round((quarters * 100 / TOTAL_QUARTERS) * 10) / 10
        };
    }

    /** Pourcentage du Coran pour un total de quarts : même règle que le serveur (quarts sur 240, une décimale). */
    function percentOfQuarters(quarters) {
        return Math.round((quarters * 100 / TOTAL_QUARTERS) * 10) / 10;
    }

    /** Date lisible (jj/mm/aaaa), calée sur UTC comme le serveur : elle ne dépend pas du fuseau de la tablette. */
    function formatDate(iso) {
        return new Date(iso).toLocaleDateString('fr-FR', { timeZone: 'UTC' });
    }

    /** Une évaluation du journal, prête à afficher en arabe : Hizb, passage d'un état à l'autre, note, auteur. */
    function describeEvaluation(entry) {
        return {
            title: LABELS.hizb + ' ' + entry.hizbNumber,
            change: LABELS.from + ' ' + (quarterLabel(entry.quartersBefore)) + ' ' + LABELS.to + ' ' + (quarterLabel(entry.quartersAfter)),
            rating: entry.rating == null ? null : entry.rating + '/5',
            by: entry.evaluatedBy || null,
            baseline: !!entry.isBaseline,
            date: formatDate(entry.evaluatedAt)
        };
    }

    /** Classes Tailwind d'une case de Hizb selon son état — trois états, trois teintes, jamais une couleur seule. */
    function cellClasses(state) {
        switch (state) {
            case 'Completed': return 'bg-emerald-100 border-emerald-500 text-emerald-900';
            case 'InProgress': return 'bg-amber-50 border-amber-400 text-amber-900';
            default: return 'bg-white border-slate-300 text-slate-600';
        }
    }

    /** Un quart est « plein » si l'élève en a acquis au moins `index` (1 à 4). */
    function quarterFilled(cell, index) {
        return (cell.completedQuarters || 0) >= index;
    }

    /** Remplace la case du même numéro de Hizb (le serveur renvoie la case enregistrée, avec son nouveau jeton). */
    function replaceCell(cells, saved) {
        return (cells || []).map((c) => (c.hizbNumber === saved.hizbNumber ? saved : c));
    }

    /**
     * Bulletin coranique PDF d'un élève : même route et même nom de fichier que sur /suivi-coranique (progress-dashboard.js).
     * Les deux écrans ne chargent pas les mêmes scripts, d'où ces deux lignes répétées plutôt qu'un couplage entre eux.
     */
    function reportUrl(studentId) {
        return `/api/v1/internat/students/${studentId}/hizb-report/pdf`;
    }

    function reportFileName(matricule) {
        return 'Bulletin-Coranique-' + String(matricule || '').replace(/[^A-Za-z0-9_-]/g, '') + '.pdf';
    }

    window.halqaLogic = {
        HIZB_COUNT, TOTAL_QUARTERS, LABELS,
        pickName, juzOfHizb, groupByJuz, quarterLabel, summarize, cellClasses, quarterFilled, replaceCell,
        reportUrl, reportFileName, percentOfQuarters, formatDate, describeEvaluation
    };

    document.addEventListener('alpine:init', () => {
        Alpine.data('halqaPage', () => ({
            // Modale de prévisualisation PDF commune (wwwroot/js/pdf-preview.js) : bulletin coranique de l'élève ouvert.
            ...window.pdfPreview.state(),

            labels: LABELS,
            loading: true,
            error: null,
            notice: null,        // information non bloquante (conflit résolu par rechargement)
            noHalqa: false,      // compte sans fiche d'Oustaz, ou fiche suspendue : rien à afficher, ce n'est pas une panne
            halqa: null,         // HalqaDto de l'Oustaz connecté

            selected: null,      // HalqaStudentDto de l'élève ouvert
            gridLoading: false,
            grid: null,          // StudentHizbProgressDto
            groups: [],          // la grille regroupée par Juz

            drawer: { open: false, cell: null, juz: null, quarters: 0, rating: null, saving: false, error: null },

            // Évolution de l'élève ouvert (journal des évaluations) : courbe + dernières évaluations, période au choix.
            history: null,
            historyLoading: false,
            historyError: null,
            historyPeriod: '6m',
            periods: LABELS.periods,

            name: pickName,
            quarterLabel,
            cellClasses,
            quarterFilled,

            async init() {
                await this.loadHalqa();
            },

            async loadHalqa() {
                this.loading = true;
                this.error = null;
                this.noHalqa = false;
                try {
                    this.halqa = await window.api.get('/internat/my-halqa');
                } catch (err) {
                    // 403 : pas de fiche d'Oustaz rattachée au compte, ou fiche suspendue — le serveur a déjà ouvert la
                    // modale « Accès refusé » avec la consigne ; l'écran affiche simplement l'état vide.
                    if (err && (err.status === 403 || err.handledGlobally)) {
                        this.noHalqa = true;
                    } else {
                        this.error = window.api.toMessage(err, LABELS.loadError);
                    }
                } finally {
                    this.loading = false;
                }
            },

            async openStudent(student) {
                this.selected = student;
                this.notice = null;
                this.history = null;
                this.historyError = null;
                this.closeDrawer();
                // La grille de saisie ne dépend pas de l'évolution : une panne du journal ne la bloque pas.
                await Promise.all([this.loadGrid(), this.loadHistory()]);
            },

            /** Retour à la liste : on relit la Halqa, car les indicateurs des cartes ont pu changer pendant la saisie. */
            async closeStudent() {
                this.selected = null;
                this.grid = null;
                this.groups = [];
                this.history = null;
                this.historyError = null;
                this.historyLoading = false;
                this.notice = null;
                this.closeDrawer();
                await this.loadHalqa();
            },

            async loadGrid() {
                if (!this.selected) return;
                this.gridLoading = true;
                this.error = null;
                try {
                    this.grid = await window.api.get(`/internat/students/${this.selected.studentId}/hizb-progress`);
                    this.groups = groupByJuz(this.grid.hizbs);
                } catch (err) {
                    this.error = window.api.toMessage(err, LABELS.gridError);
                } finally {
                    this.gridLoading = false;
                }
            },

            async loadHistory() {
                if (!this.selected) return;
                const studentId = this.selected.studentId;
                const { from, to } = window.trendChart.periodRange(this.historyPeriod);
                this.historyLoading = true;
                this.historyError = null;
                try {
                    const query = (from ? 'from=' + from + '&' : '') + 'to=' + to;
                    const result = await window.api.get(`/internat/students/${studentId}/hizb-history?${query}`);
                    // Réponse tardive d'un élève déjà refermé ou remplacé : on ne l'affiche pas chez le suivant.
                    if (this.selected && this.selected.studentId === studentId) this.history = result;
                } catch (err) {
                    this.historyError = LABELS.historyError;
                } finally {
                    this.historyLoading = false;
                }
            },

            async setHistoryPeriod(key) {
                if (key === this.historyPeriod) return;
                this.historyPeriod = key;
                await this.loadHistory();
            },

            /** Tracé de la courbe : niveau d'ouverture de la période (s'il existe), puis un point par jour de saisie. */
            get historyChart() {
                const h = this.history;
                if (!h) return window.trendChart.build([]);
                const at = (day) => Date.parse(day + 'T12:00:00Z');
                const points = h.points.map((p) => ({ t: at(p.date), v: p.progressPercent }));
                if (h.from && h.openingTotalQuarters != null) {
                    points.unshift({ t: Date.parse(h.from + 'T00:00:00Z'), v: percentOfQuarters(h.openingTotalQuarters) });
                }
                return window.trendChart.build(points, {
                    fromMs: h.from ? Date.parse(h.from + 'T00:00:00Z') : undefined,
                    toMs: h.to ? Date.parse(h.to + 'T23:59:59Z') : undefined,
                    formatX: (t) => formatDate(new Date(t).toISOString())
                });
            },

            /** La courbe en balise <svg> (chaîne pour x-html) : une <template x-for> ne marche pas dans un SVG. */
            get historySvg() {
                return window.trendChart.svg(this.historyChart, { label: LABELS.historyChart });
            },

            /** Aucune évaluation à tracer : on le dit plutôt que de dessiner une ligne plate à 0 %. */
            get historyEmpty() {
                return !!this.history && this.historyChart.dots.length === 0;
            },

            /** Date de début du journal, quand elle tombe DANS la période : avant, il n'y a pas d'historique à montrer. */
            get historySince() {
                const h = this.history;
                if (!h || !h.firstEvaluationAt || !h.from) return null;
                return new Date(h.firstEvaluationAt) > new Date(h.from + 'T00:00:00Z') ? formatDate(h.firstEvaluationAt) : null;
            },

            get historyEntries() {
                return this.history ? this.history.recent.map(describeEvaluation) : [];
            },

            /**
             * Bulletin PDF de l'élève ouvert. Indépendant de la grille : le document est produit par le serveur, qui applique
             * la même portée que l'écran (un Oustaz ne tire que le bulletin de SES élèves, 403 sinon).
             */
            async openReport() {
                if (!this.selected) return;
                await this.openPdfPreview(
                    reportUrl(this.selected.studentId),
                    'Bulletin coranique — ' + this.selected.fullName,
                    reportFileName(this.selected.matricule)
                );
            },

            // --- Tiroir de saisie -------------------------------------------------------------------

            openDrawer(cell) {
                this.drawer = {
                    open: true, cell, juz: juzOfHizb(cell.hizbNumber),
                    quarters: cell.completedQuarters, rating: cell.rating ?? null, saving: false, error: null
                };
            },

            closeDrawer() {
                this.drawer = { open: false, cell: null, juz: null, quarters: 0, rating: null, saving: false, error: null };
            },

            pickQuarters(quarters) {
                this.drawer.quarters = quarters;
                // Un Hizb non commencé n'a pas de note : le serveur la refuse (422), donc on la retire d'emblée.
                if (quarters === 0) this.drawer.rating = null;
            },

            pickRating(rating) {
                if (this.drawer.quarters === 0) return;
                // Retoucher la note déjà choisie l'annule : la note est facultative.
                this.drawer.rating = this.drawer.rating === rating ? null : rating;
            },

            /** Enregistrer n'a de sens que si quelque chose a changé par rapport à la case ouverte. */
            get canSave() {
                const d = this.drawer;
                if (!d.open || !d.cell || d.saving) return false;
                return d.quarters !== d.cell.completedQuarters || (d.rating ?? null) !== (d.cell.rating ?? null);
            },

            async save() {
                if (!this.canSave) return;
                const d = this.drawer;
                d.saving = true;
                d.error = null;
                try {
                    const saved = await window.api.put(`/internat/students/${this.selected.studentId}/hizb-progress`, {
                        hizbNumber: d.cell.hizbNumber,
                        completedQuarters: d.quarters,
                        rating: d.quarters === 0 ? null : d.rating,
                        rowVersion: d.cell.rowVersion ?? null
                    });
                    // Le serveur renvoie la case enregistrée (état et date décidés par lui, nouveau jeton) : on la
                    // substitue, puis on recalcule l'en-tête avec la même règle que lui.
                    this.grid.hizbs = replaceCell(this.grid.hizbs, saved);
                    this.grid.summary = summarize(this.grid.hizbs);
                    this.groups = groupByJuz(this.grid.hizbs);
                    this.closeDrawer();
                    await this.loadHistory();
                } catch (err) {
                    if (err && err.status === 409) {
                        // Quelqu'un d'autre a modifié ce Hizb : on ne réécrit pas par-dessus, on relit la grille.
                        this.closeDrawer();
                        this.notice = LABELS.conflict;
                        await Promise.all([this.loadGrid(), this.loadHistory()]);
                        return;
                    }
                    d.error = window.api.toMessage(err, LABELS.saveError);
                    d.saving = false;
                }
            }
        }));
    });
})();
