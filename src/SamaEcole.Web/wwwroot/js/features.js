/**
 * Contrôle d'accès par formule, côté affichage — dépend de api.js, à charger après lui.
 *
 * CONFORT D'AFFICHAGE UNIQUEMENT : la vraie protection est [RequireFeature] côté serveur (voir
 * FeatureAuthorizationHandler). Masquer un bouton ici n'empêche personne d'appeler l'API — cela
 * évite simplement de proposer une action qui se solderait par un 403 FEATURE_NOT_IN_PLAN.
 *
 * Chargé UNE fois par page et mémorisé : la formule ne change pas en cours de session, et chaque
 * écran qui interroge `has(...)` ne doit pas déclencher son propre aller-retour.
 */
window.features = {
    plan: null,
    enabled: [],
    isLoaded: false,
    _pending: null,

    /** Idempotent : les appels concurrents partagent la même promesse, un seul appel réseau. */
    async load() {
        if (this.isLoaded) return this;
        if (this._pending) return this._pending;

        this._pending = (async () => {
            try {
                const result = await window.api.get('/features');
                this.plan = result.plan || null;
                this.enabled = result.features || [];
            } catch {
                // silence-volontaire: échec FERMÉ, par sécurité. Session absente ou réseau coupé : on
                // reste sur « aucune option ». Ne jamais ouvrir une fonctionnalité facturée parce que
                // sa vérification a échoué — l'API refuserait de toute façon, autant ne pas la
                // proposer. Un toast au chargement de CHAQUE page serait par ailleurs intenable.
                this.plan = null;
                this.enabled = [];
            } finally {
                this.isLoaded = true;
                this._pending = null;
            }
            return this;
        })();

        return this._pending;
    },

    has(feature) {
        return this.enabled.includes(feature);
    },

    /** Formule minimale requise — miroir de PlanFeatures.MinimumPlanFor, pour le libellé du badge. */
    requiredPlanFor(feature) {
        return feature === 'AdvancedFinancialReports' ? 'Standard' : 'Premium';
    },

    upgradeLabel(feature) {
        return `Réservé à la formule ${this.requiredPlanFor(feature)}`;
    }
};

/**
 * Composant Alpine partagé : `x-data="featureGate('SmsNotifications')"` expose `allowed`, et
 * `label` pour le badge d'incitation. Les écrans s'en servent pour désactiver un bouton plutôt que
 * de le faire disparaître — une fonctionnalité invisible ne se vend pas.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('featureGate', (feature) => ({
        allowed: false,
        label: window.features.upgradeLabel(feature),

        async init() {
            await window.features.load();
            this.allowed = window.features.has(feature);
        }
    }));
});
