/**
 * Guichet PayDunya SIMULÉ (Dev uniquement — voir DevPaymentService/DevPaymentSimulationController).
 *
 * Rejoue exactement ce qu'un vrai callback PayDunya déclencherait : POST sur la MÊME route publique
 * (/api/v1/webhooks/payments/PayDunya) que l'agrégateur réel appellerait. Le paiement est confirmé par
 * ProcessPaymentWebhookHandler — jamais par ce script directement (AGENTS.md règle #11 : aucune route
 * accessible au client ne positionne Confirmed).
 */
(async () => {
    const root = document.getElementById('dev-checkout');
    const status = document.getElementById('dev-checkout-status');
    const internalPaymentId = root.dataset.internalPaymentId;

    // Faux délai : laisse le temps de lire l'écran, comme un vrai guichet de paiement le ferait.
    await new Promise(resolve => setTimeout(resolve, 1200));

    try {
        const response = await fetch('/api/v1/webhooks/payments/PayDunya', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ internalPaymentId, signatureValid: true })
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        status.textContent = 'Paiement confirmé — redirection…';
        window.location.assign('/');
    } catch {
        status.textContent = "Échec de la simulation de paiement. Réessayez depuis l'écran précédent.";
        status.classList.add('text-danger');
    }
})();
