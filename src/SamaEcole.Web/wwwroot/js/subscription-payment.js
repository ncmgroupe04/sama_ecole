/**
 * Écran /abonnement/paiement — initiation du paiement d'abonnement (ticket JGK-I05).
 *
 * Le Directeur choisit un moyen de paiement et une période, puis est redirigé vers le guichet sécurisé
 * de l'agrégateur (PayDunya). Aucune confirmation de paiement ne se décide ici (AGENTS.md règle #11) :
 * cet écran ne fait qu'INITIER — seul le webhook signé (JGK-I06, à venir) confirmera un jour le paiement.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('subscriptionPaymentForm', () => ({
        method: 'MobileMoney',
        // Annuel par défaut : la grille tarifaire est ANNUELLE. Le serveur facture l'annuel à tout établissement issu
        // d'une demande d'inscription, même si « Mensuel » est demandé (SubscriptionAmountResolver).
        billingPeriod: 'Yearly',
        isSubmitting: false,
        error: null,

        // Module Tarification & Promotions — aperçu uniquement (ValidatePromoCodeQuery, LECTURE
        // SEULE). Le montant qui compte réellement est RECALCULÉ côté serveur à submit() : promoResult
        // n'est affiché qu'à titre indicatif, jamais transmis tel quel au paiement.
        promoCode: '',
        promoResult: null,
        promoError: null,
        isCheckingPromo: false,

        async applyPromoCode() {
            this.isCheckingPromo = true;
            this.promoError = null;
            this.promoResult = null;

            try {
                const result = await window.api.post('/subscriptions/validate-promo', {
                    code: this.promoCode,
                    billingPeriod: this.billingPeriod
                });

                if (!result.isValid) {
                    this.promoError = window.api.toMessage(result, 'Ce code promo est invalide.');
                } else {
                    this.promoResult = result;
                }
            } catch (err) {
                this.promoError = window.api.toMessage(err, 'Vérification du code promo impossible.');
            } finally {
                this.isCheckingPromo = false;
            }
        },

        formatXof(amount) {
            if (amount === null || amount === undefined) return '';
            return new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'XOF', maximumFractionDigits: 0 }).format(amount);
        },

        async submit() {
            this.error = null;
            this.isSubmitting = true;

            try {
                const schoolId = window.auth.schoolId;

                const result = await window.api.post(`/subscriptions/${schoolId}/payments`, {
                    method: this.method,
                    billingPeriod: this.billingPeriod,
                    promoCode: this.promoCode || null
                });

                if (result.activatedWithoutPayment) {
                    // Code promo FreeTrialMonths/FullDiscount : aucun guichet à ouvrir, l'abonnement
                    // est déjà Actif (AGENTS.md règle #11 — pas de paiement, donc pas de webhook à
                    // attendre). require-session.js/api.js redirigera vers l'app dès le prochain appel.
                    window.location.assign('/');
                    return;
                }

                // Quitte l'application : le guichet PayDunya est hébergé par l'agrégateur, pas par nous.
                window.location.assign(result.redirectUrl);
            } catch (err) {
                this.error = window.api.toMessage(err, "Initiation du paiement impossible. Vérifiez votre réseau et réessayez.");
                this.isSubmitting = false;
            }
            // Pas de `finally` sur isSubmitting : en cas de succès, la page quitte de toute façon vers
            // PayDunya (ou l'app) — remettre isSubmitting à false ferait juste clignoter le bouton avant
            // le départ.
        }
    }));
});
