/**
 * Échéancier personnalisé d'une inscription (POST /finance/enrollments/{id}/installment-plan) — Étape 5, recouvrement.
 *
 * Un plan négocié avec la famille REMPLACE le plan actif (l'ancien passe à « Annulé », jamais supprimé). Il ne change
 * JAMAIS le montant dû de l'inscription (règle #4) : la somme des échéances doit lui être égale, c'est le serveur qui
 * en fait foi (CreateFeeInstallmentPlanCommandHandler). Ce fichier n'aide qu'à composer le plan : lignes éditables,
 * total en direct, répartition en parts égales. `canManagePlan` n'est qu'un confort d'affichage — le serveur garde
 * (Directeur et Finance).
 *
 * Usage dans un composant Alpine disposant de `balance` (StudentBalanceDto) et de `reloadBalance()` :
 *     Alpine.data('xxx', () => ({ ...window.installmentPlan.mixin(), … }))
 */
(function () {
    const MAX_LINES = 24;

    function todayIso() {
        return new Date().toISOString().split('T')[0];
    }

    /** « 2026-10-12T00:00:00+00:00 » → « 2026-10-12 » : la date d'une échéance telle que l'API la renvoie. */
    function isoDay(value) {
        return value ? String(value).slice(0, 10) : '';
    }

    /** Ajoute `months` mois à une date ISO (jour conservé, borné au dernier jour du mois). */
    function addMonths(iso, months) {
        const [y, m, d] = iso.split('-').map(Number);
        const target = new Date(Date.UTC(y, m - 1 + months, 1));
        const lastDay = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate();
        target.setUTCDate(Math.min(d, lastDay));
        return target.toISOString().split('T')[0];
    }

    /** Répartit `total` en `count` parts entières ; le reliquat va à la première part, la somme est exacte. */
    function splitAmount(total, count) {
        const base = Math.floor(total / count);
        const parts = Array(count).fill(base);
        parts[0] += total - base * count;
        return parts;
    }

    window.installmentPlan = {
        MAX_LINES,
        addMonths,
        splitAmount,
        isoDay,

        mixin() {
            return {
                canManagePlan: ['Directeur', 'Finance'].includes(window.auth.role),

                plan: { open: false, reason: '', lines: [], splitCount: 3, isSaving: false, errors: {} },

                // Méthodes et non « get » : un getter copié par l'étalement `...mixin()` serait figé à sa valeur initiale.
                planTotal() {
                    return this.plan.lines.reduce((sum, l) => sum + (Number(l.amount) || 0), 0);
                },

                /** Écart entre le montant dû et la somme saisie : 0 = plan équilibré. */
                planGap() {
                    return (this.balance?.totalDue ?? 0) - this.planTotal();
                },

                planCanSubmit() {
                    return !this.plan.isSaving
                        && this.plan.lines.length > 0
                        && this.planGap() === 0
                        && this.plan.lines.every(l => l.label.trim() && Number(l.amount) > 0 && l.dueDate);
                },

                /** Ouvre l'éditeur, prérempli avec le calendrier actuel (à ajuster) ou une seule échéance. */
                openPlanEditor() {
                    if (!this.balance) return;
                    const current = (this.balance.installments || []).map(i => ({
                        label: i.designation, amount: Number(i.amount), dueDate: isoDay(i.dueDate)
                    }));
                    this.plan = {
                        open: true,
                        reason: '',
                        lines: current.length > 0
                            ? current
                            : [{ label: 'Échéance 1', amount: this.balance.totalDue, dueDate: todayIso() }],
                        splitCount: 3,
                        isSaving: false,
                        errors: {}
                    };
                },

                closePlanEditor() {
                    this.plan = { open: false, reason: '', lines: [], splitCount: 3, isSaving: false, errors: {} };
                },

                addPlanLine() {
                    if (this.plan.lines.length >= MAX_LINES) return;
                    const last = this.plan.lines[this.plan.lines.length - 1];
                    this.plan.lines.push({
                        label: `Échéance ${this.plan.lines.length + 1}`,
                        amount: Math.max(this.planGap(), 0),
                        dueDate: last ? addMonths(last.dueDate || todayIso(), 1) : todayIso()
                    });
                },

                removePlanLine(index) {
                    this.plan.lines.splice(index, 1);
                },

                /** N parts égales, une par mois à partir d'aujourd'hui ; la première porte le reliquat. */
                splitPlanEvenly() {
                    const count = Math.min(Math.max(parseInt(this.plan.splitCount, 10) || 1, 1), MAX_LINES);
                    const amounts = splitAmount(this.balance.totalDue, count);
                    const start = todayIso();
                    this.plan.lines = amounts.map((amount, i) => ({
                        label: `Échéance ${i + 1}`, amount, dueDate: addMonths(start, i)
                    }));
                },

                async submitPlan() {
                    if (!this.planCanSubmit()) return;
                    this.plan.isSaving = true;
                    this.plan.errors = {};
                    try {
                        await window.api.post(`/finance/enrollments/${this.balance.enrollmentId}/installment-plan`, {
                            reason: this.plan.reason.trim() || null,
                            installments: this.plan.lines.map(l => ({
                                label: l.label.trim(), amount: Number(l.amount), dueDate: l.dueDate
                            }))
                        });
                        this.closePlanEditor();
                        await this.reloadBalance();
                        window.toast?.success('Échéancier personnalisé enregistré.');
                    } catch (error) {
                        console.error('Installment plan error:', error);
                        this.plan.errors = window.api.toFieldErrors(error, "Erreur lors de l'enregistrement de l'échéancier.");
                    } finally {
                        this.plan.isSaving = false;
                    }
                }
            };
        }
    };
})();
