/**
 * Espace de l'Oustaz (/halqa) — tablette : sa Halqa, la grille des 60 Hizb de chaque élève regroupés par Juz, et le
 * tiroir de saisie des quarts. Écran en arabe, de droite à gauche (dir="rtl" posé sur la vue).
 *
 * Consomme GET /api/v1/internat/my-halqa (la Halqa de l'Oustaz connecté, résolue côté serveur depuis son compte —
 * aucun identifiant n'est passé), GET /api/v1/internat/students/{id}/hizb-progress (la grille) et
 * PUT /api/v1/internat/students/{id}/hizb-progress (un Hizb à la fois). La portée (un Oustaz ne voit et n'écrit que
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

    window.halqaLogic = {
        HIZB_COUNT, TOTAL_QUARTERS, LABELS,
        pickName, juzOfHizb, groupByJuz, quarterLabel, summarize, cellClasses, quarterFilled, replaceCell
    };

    document.addEventListener('alpine:init', () => {
        Alpine.data('halqaPage', () => ({
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
                this.closeDrawer();
                await this.loadGrid();
            },

            /** Retour à la liste : on relit la Halqa, car les indicateurs des cartes ont pu changer pendant la saisie. */
            async closeStudent() {
                this.selected = null;
                this.grid = null;
                this.groups = [];
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
                } catch (err) {
                    if (err && err.status === 409) {
                        // Quelqu'un d'autre a modifié ce Hizb : on ne réécrit pas par-dessus, on relit la grille.
                        this.closeDrawer();
                        this.notice = LABELS.conflict;
                        await this.loadGrid();
                        return;
                    }
                    d.error = window.api.toMessage(err, LABELS.saveError);
                    d.saving = false;
                }
            }
        }));
    });
})();
