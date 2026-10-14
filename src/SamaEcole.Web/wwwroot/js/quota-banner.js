/**
 * Bandeau de quota d'élèves du tableau de bord (Directeur, Secrétariat) — GET /tenant-subscription/quota.
 *
 * Trois états, décidés par le SERVEUR (StudentQuotaEvaluator) — cet écran ne recalcule rien :
 *   · WithinQuota : rien à afficher ;
 *   · InTolerance : l'effectif dépasse le plafond de la tranche, la création reste permise jusqu'à la tolérance — avertissement
 *     doux, avec l'invitation à passer à la tranche supérieure ;
 *   · Exceeded : tolérance dépassée — les nouvelles inscriptions et les imports sont bloqués, message clair vers la mise à jour.
 *
 * « Passer à la tranche X » ouvre la modale universelle « Mise à jour de votre abonnement » (subscription-change.js), qui montre le
 * tarif ajusté calculé par le serveur et envoie la demande ; le changement n'a lieu qu'une fois validé par le Super Admin.
 * Lire le quota met aussi à jour, côté serveur, la date du premier constat de dépassement (OverQuotaSince).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('quotaBanner', () => ({
        quota: null,

        async init() {
            // Directeur et Secrétariat créent des élèves : ce sont eux que le plafond concerne. Les autres rôles n'ont pas la route.
            const role = window.auth && window.auth.role;
            if (role !== 'Directeur' && role !== 'Secretariat') return;

            try {
                this.quota = (await window.api.get('/tenant-subscription/quota')) || null;
            } catch (err) {
                // Un bandeau d'information ne doit jamais casser le tableau de bord : silence en cas d'échec.
                this.quota = null;
            }
        },

        get visible() {
            return !!this.quota && this.quota.state !== 'WithinQuota';
        },

        get isExceeded() {
            return !!this.quota && this.quota.state === 'Exceeded';
        },

        get remaining() {
            return this.quota ? Math.max(0, this.quota.softQuotaLimit - this.quota.currentStudentCount) : 0;
        },

        get hasPendingRequest() {
            return !!this.quota && !!this.quota.pendingChangeRequestId;
        },

        /** Seul le Directeur envoie la demande, et seulement s'il n'y en a pas déjà une en cours et qu'une tranche convient. */
        get canRequestUpgrade() {
            return !!window.auth && window.auth.role === 'Directeur'
                && !!this.quota && !!this.quota.suggestedTier && !this.hasPendingRequest;
        },

        get title() {
            return this.isExceeded ? 'Limite d\'élèves atteinte' : 'Votre effectif dépasse votre tranche';
        },

        get message() {
            if (!this.quota) return '';
            const q = this.quota;

            if (this.isExceeded) {
                return `Votre établissement compte ${q.currentStudentCount} élèves, au-delà de la tolérance de votre tranche `
                    + `(${q.tierLabel}, tolérance jusqu'à ${q.softQuotaLimit}). Les nouvelles inscriptions et les imports d'élèves sont bloqués.`;
            }

            const base = `Votre établissement compte ${q.currentStudentCount} élèves pour une tranche ${q.tierLabel} (plafond ${q.maxStudentLimit}). `;

            // Tolérance épuisée : la prochaine inscription sera refusée — le dire plutôt que « encore 0 élève ».
            return this.remaining === 0
                ? `${base}La prochaine inscription sera bloquée (tolérance jusqu'à ${q.softQuotaLimit}).`
                : `${base}Vous pouvez encore inscrire ${this.remaining} élève${this.remaining > 1 ? 's' : ''} avant le blocage `
                    + `(tolérance jusqu'à ${q.softQuotaLimit}).`;
        },

        get upgradeLabel() {
            if (!this.quota || !this.quota.suggestedTier) return '';
            return this.quota.suggestedTier === 'Tier3_800'
                ? 'Demander l\'offre Grand Groupe'
                : `Passer à la tranche « ${this.quota.suggestedTierLabel} »`;
        },

        upgrade() {
            if (!this.canRequestUpgrade) return;
            Alpine.store('subscriptionChange').requestChange({ tier: this.quota.suggestedTier });
        }
    }));
});
