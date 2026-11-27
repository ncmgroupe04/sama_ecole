/**
 * Bannière d'expiration d'abonnement — visible sur TOUS les écrans (layout principal).
 *
 * Orange : rappel d'approche de l'échéance (réactivation conseillée).
 * Rouge  : critique — 2 jours avant l'échéance, 10 jours après, et tout le temps où l'établissement
 *          est en lecture seule (statut ReadOnly/AwaitingPayment). Ne disparaît pas tant que
 *          l'abonnement n'est pas régularisé.
 *
 * Source : GET /api/v1/subscription-status (tous les rôles de l'école).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('subscriptionExpiryBanner', () => ({
        status: null,
        expiresAt: null,
        loaded: false,

        async init() {
            if (!window.auth || !window.auth.isAuthenticated() || window.auth.role === 'SuperAdmin') {
                this.loaded = true;
                return;
            }
            try {
                const data = await window.api.get('/subscription-status');
                this.status = data && data.status ? data.status : null;
                this.expiresAt = data && data.expiresAt ? data.expiresAt : null;
            } catch (e) {
                this.status = null;
                this.expiresAt = null;
            } finally {
                this.loaded = true;
            }
        },

        get daysUntilExpiry() {
            if (!this.expiresAt) return null;
            const s = String(this.expiresAt).slice(0, 10);
            const parts = s.split('-').map(Number);
            if (parts.length !== 3 || parts.some(isNaN)) return null;
            const exp = new Date(parts[0], parts[1] - 1, parts[2]);
            exp.setHours(0, 0, 0, 0);
            const today = new Date();
            today.setHours(0, 0, 0, 0);
            return Math.round((exp - today) / (1000 * 60 * 60 * 24));
        },

        get isReadOnly() {
            return this.status === 'ReadOnly' || this.status === 'AwaitingPayment';
        },

        get showRed() {
            if (this.isReadOnly) return true;
            const d = this.daysUntilExpiry;
            if (d === null) return false;
            // 2 jours avant l'échéance et jusqu'à 10 jours après.
            return d <= 2 && d >= -10;
        },

        get showOrange() {
            if (this.showRed) return false;
            const d = this.daysUntilExpiry;
            if (d === null) return false;
            // Rappel d'approche : entre 3 et 14 jours avant l'échéance.
            return d >= 3 && d <= 14;
        },

        get visible() {
            return this.loaded && (this.showRed || this.showOrange);
        },

        get message() {
            if (this.isReadOnly) {
                return "Votre abonnement a expiré : l'accès est en lecture seule. Régularisez votre abonnement pour retrouver toutes les fonctionnalités.";
            }
            const d = this.daysUntilExpiry;
            if (d !== null && d < 0) {
                return `Votre abonnement a expiré il y a ${Math.abs(d)} jour(s). Régularisez-le pour éviter la lecture seule.`;
            }
            if (d === 0) return "Votre abonnement expire aujourd'hui. Renouvelez-le pour éviter toute interruption.";
            if (d === 1) return "Votre abonnement expire demain. Renouvelez-le dès maintenant.";
            if (d !== null) return `Votre abonnement expire dans ${d} jours. Pensez à le renouveler.`;
            return "Pensez à renouveler votre abonnement.";
        },

        goPay() {
            window.location.assign('/parametres?tab=facturation');
        }
    }));
});