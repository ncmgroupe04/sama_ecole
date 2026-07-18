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
        billingPeriod: 'Monthly',
        isSubmitting: false,
        error: null,

        async submit() {
            this.error = null;
            this.isSubmitting = true;

            try {
                const schoolId = window.auth.schoolId;

                const result = await window.api.post(`/subscriptions/${schoolId}/payments`, {
                    method: this.method,
                    billingPeriod: this.billingPeriod
                });

                // Quitte l'application : le guichet PayDunya est hébergé par l'agrégateur, pas par nous.
                window.location.assign(result.redirectUrl);
            } catch (err) {
                this.error = err.message || "Initiation du paiement impossible. Vérifiez votre réseau et réessayez.";
                this.isSubmitting = false;
            }
            // Pas de `finally` sur isSubmitting : en cas de succès, la page quitte de toute façon vers
            // PayDunya — remettre isSubmitting à false ferait juste clignoter le bouton avant le départ.
        }
    }));
});
