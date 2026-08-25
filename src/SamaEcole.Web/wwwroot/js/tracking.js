/**
 * Suivi PUBLIC d'une demande d'inscription — ticket JGK-I02.
 *
 * Page entièrement anonyme : la référence de suivi (saisie ou reçue en ?ref=) fait office de secret
 * d'accès de fait à GET /api/v1/registration-requests/{ref}/status. Aucune session, aucun jeton.
 */
(() => {
    'use strict';

    const API_BASE = '/api/v1';

    async function toError(response) {
        const payload = await response.json().catch(() => null);

        if (payload && payload.message) {
            const error = new Error(payload.message);
            error.code = payload.code;
            error.status = response.status;
            return error;
        }

        const fallback = new Error(`Erreur HTTP ${response.status}`);
        fallback.status = response.status;
        return fallback;
    }

    window.tracking = {
        async fetchStatus(trackingReference) {
            const response = await fetch(
                `${API_BASE}/registration-requests/${encodeURIComponent(trackingReference)}/status`);

            if (!response.ok) {
                throw await toError(response);
            }

            return await response.json();
        }
    };
})();

document.addEventListener('alpine:init', () => {
    Alpine.data('trackingForm', () => ({
        trackingReference: '',
        isSubmitting: false,
        error: null,
        result: null,

        init() {
            // Pré-remplissage depuis le lien affiché en fin de formulaire d'inscription (?ref=REG-...) :
            // évite au Directeur de recopier sa référence à la main juste après l'avoir reçue.
            const prefilled = new URLSearchParams(window.location.search).get('ref');

            if (prefilled) {
                this.trackingReference = prefilled;
                this.submit();
            }
        },

        async submit() {
            this.error = null;
            this.result = null;

            if (!this.trackingReference.trim()) {
                this.error = 'Merci de saisir votre référence de suivi.';
                return;
            }

            this.isSubmitting = true;

            try {
                this.result = await window.tracking.fetchStatus(this.trackingReference.trim());
            } catch (err) {
                if (err.status === 404) {
                    this.error = 'Aucune demande ne correspond à cette référence. Vérifiez la saisie.';
                } else if (err.status === 429) {
                    this.error = 'Trop de vérifications depuis votre connexion. Réessayez dans quelques minutes.';
                } else {
                    this.error = window.api.toMessage(err, 'Vérification impossible. Vérifiez votre réseau et réessayez.');
                }
            } finally {
                this.isSubmitting = false;
            }
        },

        // Libellés/couleurs du badge de statut — un seul endroit à mettre à jour si l'énumération change.
        statusLabel(status) {
            return { Pending: 'En attente', Approved: 'Approuvée', Rejected: 'Rejetée' }[status] || status;
        },

        statusBadgeClass(status) {
            return {
                Pending: 'bg-warning-bg text-warning',
                Approved: 'bg-success-bg text-success',
                Rejected: 'bg-danger-bg text-danger'
            }[status] || 'bg-gray-100 text-gray-700';
        },

        formatDate(iso) {
            if (!iso) return '';
            return new Date(iso).toLocaleDateString('fr-FR');
        }
    }));
});
