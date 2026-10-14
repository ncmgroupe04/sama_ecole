/**
 * Console Super Admin — Changements d'abonnement. Consomme :
 *   GET  /admin/subscription-change-requests?status=…   les demandes de TOUTES les écoles (SubscriptionChangeRequestDto)
 *   POST /admin/subscription-change-requests/{id}/approve | /reject   (décision ; motif obligatoire pour un rejet)
 *   GET  /admin/over-quota-schools                      les écoles dont l'effectif dépasse le plafond de leur tranche
 *
 * Cet écran n'écrit JAMAIS l'état d'une école lui-même : « Valider » demande au serveur d'appliquer l'état cible STOCKÉ sur la
 * demande (profil, tranche, cycles, modules), après des gardes que le serveur applique (état périmé, effectif, classes). Les
 * montants affichés sont ceux annoncés au Directeur à la date de la demande.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('superAdminSubscriptionChanges', () => ({
        requests: [],
        overQuota: [],
        statusFilter: 'Pending',
        isLoading: false,
        error: null,

        decisionTarget: null,
        decisionMode: null,
        decisionNote: '',
        decisionError: null,
        isDeciding: false,

        // Résultat de la dernière décision : le Directeur est prévenu par e-mail ; si l'envoi échoue, la décision reste valable mais
        // c'est au Super Admin de le prévenir lui-même.
        notice: null,
        noticeIsWarning: false,

        statusOptions: [
            { value: 'Pending', label: 'En attente' },
            { value: 'Approved', label: 'Validées' },
            { value: 'Rejected', label: 'Rejetées' },
            { value: 'Cancelled', label: 'Retirées' },
            { value: '', label: 'Toutes' }
        ],

        async load() {
            this.isLoading = true;
            this.error = null;

            try {
                const [requests, overQuota] = await Promise.all([
                    window.api.get(this.requestsUrl()),
                    window.api.get('/admin/over-quota-schools')
                ]);
                this.requests = requests;
                this.overQuota = overQuota;
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des changements d\'abonnement.');
            } finally {
                this.isLoading = false;
            }
        },

        requestsUrl() {
            return this.statusFilter
                ? `/admin/subscription-change-requests?status=${encodeURIComponent(this.statusFilter)}`
                : '/admin/subscription-change-requests';
        },

        async selectStatus(value) {
            this.statusFilter = value;
            this.error = null;

            try {
                this.requests = await window.api.get(this.requestsUrl());
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des demandes.');
            }
        },

        get pendingCount() {
            return this.requests.filter((r) => r.status === 'Pending').length;
        },

        // ------------------------------------------------------------------ Affichage

        formatXof(amount) {
            if (amount === null || amount === undefined) return '—';
            return new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'XOF', maximumFractionDigits: 0 }).format(amount);
        },

        formatSigned(amount) {
            if (amount === null || amount === undefined) return '—';
            return (amount > 0 ? '+' : amount < 0 ? '−' : '') + this.formatXof(Math.abs(amount));
        },

        formatDate(iso) {
            if (!iso) return '—';
            return new Date(iso).toLocaleDateString('fr-FR');
        },

        statusLabel(status) {
            return { Pending: 'En attente', Approved: 'Validée', Rejected: 'Rejetée', Cancelled: 'Retirée' }[status] || status;
        },

        statusClasses(status) {
            return {
                Pending: 'bg-amber-500/15 text-amber-400 ring-1 ring-inset ring-amber-500/20',
                Approved: 'bg-emerald-500/15 text-emerald-400 ring-1 ring-inset ring-emerald-500/20',
                Rejected: 'bg-rose-500/15 text-rose-400 ring-1 ring-inset ring-rose-500/20',
                Cancelled: 'bg-zinc-800 text-zinc-400'
            }[status] || 'bg-zinc-800 text-zinc-400';
        },

        /** Le tarif annoncé au Directeur : écart connu, tarif cible seul, ou « sur devis / à chiffrer ». */
        priceLine(request) {
            if (request.differenceMonthlyXof !== null && request.differenceMonthlyXof !== undefined) {
                return `${this.formatSigned(request.differenceMonthlyXof)} / mois · ${this.formatSigned(request.differenceAnnualXof)} / an`;
            }
            if (request.targetMonthlyXof !== null && request.targetMonthlyXof !== undefined) {
                return `Nouveau tarif ${this.formatXof(request.targetMonthlyXof)} / mois — écart à chiffrer`;
            }
            return 'Tarif à établir (sur devis ou ancienne grille)';
        },

        /** « jusqu'à 100 élèves » plutôt que l'identifiant d'API « Tier_100 ». */
        tierLabel(tier) {
            return {
                Tier_100: "jusqu'à 100 élèves",
                Tier1_150: "jusqu'à 150 élèves",
                Tier_250: "jusqu'à 250 élèves",
                Tier2_400: "jusqu'à 400 élèves",
                Tier3_800: 'Grand Groupe (sur devis)',
                Tier4_Custom: 'Sur mesure (sur devis)'
            }[tier] || tier;
        },

        overQuotaStateLabel(state) {
            return state === 'Exceeded' ? 'Limite dépassée (création bloquée)' : 'Dans la tolérance';
        },

        overQuotaStateClasses(state) {
            return state === 'Exceeded'
                ? 'bg-rose-500/15 text-rose-400 ring-1 ring-inset ring-rose-500/20'
                : 'bg-amber-500/15 text-amber-400 ring-1 ring-inset ring-amber-500/20';
        },

        // ------------------------------------------------------------------ Décision

        openDecision(request, mode) {
            this.decisionTarget = request;
            this.decisionMode = mode;
            this.decisionNote = '';
            this.decisionError = null;
        },

        closeDecision() {
            this.decisionTarget = null;
            this.decisionMode = null;
        },

        get decisionTitle() {
            if (!this.decisionTarget) return '';
            return `${this.decisionMode === 'approve' ? 'Valider' : 'Rejeter'} la demande — ${this.decisionTarget.schoolName || ''}`;
        },

        async confirmDecision() {
            if (!this.decisionTarget || this.isDeciding) return;

            // Le motif d'un rejet est obligatoire : le Directeur le lit. Le serveur le vérifie aussi (422).
            if (this.decisionMode === 'reject' && !this.decisionNote.trim()) {
                this.decisionError = 'Indiquez le motif du rejet : le Directeur le verra.';
                return;
            }

            this.isDeciding = true;
            this.decisionError = null;

            try {
                const decided = await window.api.post(
                    `/admin/subscription-change-requests/${this.decisionTarget.id}/${this.decisionMode}`,
                    { note: this.decisionNote.trim() || null });

                this.noticeIsWarning = !decided || decided.directorNotified !== true;
                this.notice = this.noticeIsWarning
                    ? "Décision enregistrée, mais l'e-mail n'a pas pu être envoyé au Directeur : prévenez-le directement."
                    : 'Décision enregistrée. Le Directeur a été prévenu par e-mail.';

                this.closeDecision();
                await this.load();
            } catch (err) {
                // 409 : l'état de l'école a changé depuis la demande, effectif trop élevé, cycle qui contient des classes… — le
                // message du serveur l'explique ; la demande reste en attente.
                this.decisionError = window.api.toMessage(err, 'Impossible d\'enregistrer la décision. Réessayez.');
            } finally {
                this.isDeciding = false;
            }
        }
    }));
});
