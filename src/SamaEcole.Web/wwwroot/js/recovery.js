/**
 * Recouvrement — lots de relance SMS des débiteurs (/recouvrement). Le serveur calcule chaque nuit un lot
 * BROUILLON par classe (DebtorAgingHostedService) dès que « Relances d'impayés » est activé dans
 * Paramètres › Notifications SMS ; cet écran en est la seule porte d'entrée humaine : on y relit
 * nommément les destinataires, on écarte les cas litigieux puis on déclenche l'envoi — jamais
 * automatiquement. Réservé au Directeur et à la Finance (garde réelle : FinanceController, 403).
 *
 * Aucune règle métier ici : le serveur fixe les destinataires, le montant et le texte du SMS ; l'écran ne
 * fait qu'afficher, confirmer et relayer l'action.
 */
document.addEventListener('alpine:init', () => {
    const STATUS_LABELS = { Draft: 'À valider', Sent: 'Envoyé', Dismissed: 'Écarté' };
    const STATUS_BADGES = { Draft: 'status-badge-warning', Sent: 'status-badge-success', Dismissed: 'status-badge-neutral' };

    Alpine.data('recoveryView', () => ({
        batches: [],
        isLoading: false,
        error: null,

        // Filtre de statut envoyé au serveur ('' = tous). Les brouillons sont ce qu'il y a à traiter.
        statusFilter: 'Draft',
        statusOptions: [
            { value: 'Draft', label: 'À valider' },
            { value: 'Sent', label: 'Envoyés' },
            { value: 'Dismissed', label: 'Écartés' },
            { value: '', label: 'Tous' }
        ],

        expandedId: null,
        isRecomputing: false,
        notice: null,

        // Fenêtre de confirmation d'envoi (action irréversible : un lot ne s'envoie qu'une fois).
        sendTarget: null,
        isSending: false,
        sendError: '',

        // Fenêtre de confirmation d'écartement.
        dismissTarget: null,
        isDismissing: false,
        dismissError: '',

        init() {
            this.load();
        },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                const query = this.statusFilter ? `?status=${encodeURIComponent(this.statusFilter)}` : '';
                this.batches = await window.api.get(`/finance/dues-reminder-batches${query}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des lots de relance.');
                this.batches = [];
            } finally {
                this.isLoading = false;
            }
        },

        selectStatus(value) {
            this.statusFilter = value;
            this.expandedId = null;
            return this.load();
        },

        statusLabel(status) {
            return STATUS_LABELS[status] || status;
        },

        statusBadge(status) {
            return STATUS_BADGES[status] || 'status-badge-neutral';
        },

        isDraft(batch) {
            return Boolean(batch) && batch.status === 'Draft';
        },

        toggle(batch) {
            this.expandedId = this.expandedId === batch.id ? null : batch.id;
        },

        /** Montant total en retard du lot — somme des reliquats des élèves (le SMS annonce ce reliquat). */
        batchTotal(batch) {
            return (batch.items || []).reduce((sum, item) => sum + Number(item.remainingBalance || 0), 0);
        },

        /** Élèves dont le tuteur n'a pas de numéro : le SMS sera écarté pour eux à l'envoi, on le dit avant. */
        withoutPhone(batch) {
            return (batch.items || []).filter((item) => !item.guardianPhone || !String(item.guardianPhone).trim()).length;
        },

        phoneLabel(item) {
            if (!item.guardianPhone || !String(item.guardianPhone).trim()) return 'Aucun numéro';
            return typeof window.formatSenegalPhoneOr === 'function'
                ? window.formatSenegalPhoneOr(item.guardianPhone)
                : item.guardianPhone;
        },

        money(amount) {
            return typeof window.formatFCFA === 'function' ? window.formatFCFA(amount) : `${amount} FCFA`;
        },

        formatDate(value) {
            if (!value) return '—';
            const d = new Date(value);
            return Number.isNaN(d.getTime())
                ? '—'
                : d.toLocaleDateString('fr-FR', { day: '2-digit', month: 'long', year: 'numeric' });
        },

        // --------------------------------------------------------------------- Recalcul
        async recompute() {
            this.isRecomputing = true;
            this.error = null;
            this.notice = null;
            try {
                const created = await window.api.post('/finance/dues-reminder-batches/recompute', {});
                this.notice = created > 0
                    ? `${created} lot(s) de relance préparé(s) en brouillon.`
                    : 'Aucun nouveau lot : aucun élève n\'est au-delà du seuil de retard, ou les lots existent déjà.';
                await this.load();
            } catch (err) {
                this.error = window.api.toMessage(err, 'Impossible de recalculer les lots de relance.');
            } finally {
                this.isRecomputing = false;
            }
        },

        // --------------------------------------------------------------------- Envoi
        openSend(batch) {
            this.sendTarget = batch;
            this.sendError = '';
        },

        closeSend() {
            if (this.isSending) return;
            this.sendTarget = null;
            this.sendError = '';
        },

        async confirmSend() {
            if (!this.sendTarget || this.isSending) return;

            this.isSending = true;
            this.sendError = '';
            try {
                const result = await window.api.post(`/finance/dues-reminder-batches/${this.sendTarget.id}/send`, {});
                const skipped = result.skippedCount > 0
                    ? ` ${result.skippedCount} écarté(s)${result.firstSkipReason ? ` (${result.firstSkipReason})` : ''}.`
                    : '';
                this.notice = `Lot « ${this.sendTarget.classroomName} » : ${result.queuedCount} SMS mis en file d'envoi.${skipped}`;
                this.sendTarget = null;
                this.expandedId = null;
                await this.load();
            } catch (err) {
                // 409 : lot déjà envoyé ou écarté par un collègue — on recharge pour refléter l'état réel.
                this.sendError = window.api.toMessage(err, 'Envoi impossible.');
                if (err && err.status === 409) await this.load();
            } finally {
                this.isSending = false;
            }
        },

        // --------------------------------------------------------------------- Écartement
        openDismiss(batch) {
            this.dismissTarget = batch;
            this.dismissError = '';
        },

        closeDismiss() {
            if (this.isDismissing) return;
            this.dismissTarget = null;
            this.dismissError = '';
        },

        async confirmDismiss() {
            if (!this.dismissTarget || this.isDismissing) return;

            this.isDismissing = true;
            this.dismissError = '';
            try {
                await window.api.post(`/finance/dues-reminder-batches/${this.dismissTarget.id}/dismiss`, {});
                this.notice = `Lot « ${this.dismissTarget.classroomName} » écarté : aucun SMS n'est envoyé.`;
                this.dismissTarget = null;
                this.expandedId = null;
                await this.load();
            } catch (err) {
                this.dismissError = window.api.toMessage(err, 'Impossible d\'écarter ce lot.');
                if (err && err.status === 409) await this.load();
            } finally {
                this.isDismissing = false;
            }
        }
    }));
});
