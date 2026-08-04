/**
 * Formulaire PUBLIC d'inscription self-service — ticket JGK-I01.
 *
 * Page entièrement anonyme : aucune session, aucun jeton. On poste vers POST /api/v1/registration-requests
 * et on affiche la référence de suivi retournée. Le mot de passe choisi ne transite QUE dans cette requête
 * HTTPS et n'est jamais restocké côté navigateur.
 *
 * `website` est le champ HONEYPOT : invisible pour un humain, laissé vide. Un bot de remplissage
 * automatique le renseigne et trahit sa nature — le serveur ignore alors silencieusement la soumission.
 */
(() => {
    'use strict';

    const API_BASE = '/api/v1';

    /** Traduit une réponse d'erreur en Error exploitable (corps JSON normalisé, ou vide). */
    async function toError(response) {
        const payload = await response.json().catch(() => null);

        if (payload && payload.message) {
            const error = new Error(payload.message);
            error.code = payload.code;
            error.details = payload.details;
            error.status = response.status;
            return error;
        }

        const fallback = new Error(`Erreur HTTP ${response.status}`);
        fallback.status = response.status;
        return fallback;
    }

    window.registration = {
        async submit(payload) {
            const response = await fetch(`${API_BASE}/registration-requests`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                throw await toError(response);
            }

            return await response.json();
        }
    };
})();

document.addEventListener('alpine:init', () => {
    Alpine.data('registrationForm', () => ({
        // Directeur
        directorFullName: '',
        directorEmail: '',
        directorPhone: '',
        directorPassword: '',
        showPassword: false,

        // Établissement
        schoolName: '',
        schoolAddress: '',
        city: '',
        region: '',
        estimatedStudentCount: '',
        requestedPlan: 'Standard',

        // Honeypot : DOIT rester vide. Lié au champ leurre `website`.
        website: '',

        isSubmitting: false,
        error: null,
        trackingReference: null,

        // Pré-remplit la formule quand on arrive depuis une carte tarifaire de la vitrine
        // (/inscription?plan=Premium) — une simple commodité d'affichage, jamais fait confiance
        // côté serveur : CreateRegistrationRequestCommand revalide requestedPlan indépendamment.
        init() {
            const plan = new URLSearchParams(window.location.search).get('plan');
            if (['Primaire', 'Standard', 'Premium'].includes(plan)) {
                this.requestedPlan = plan;
            }
        },

        async submit() {
            this.error = null;
            this.isSubmitting = true;

            try {
                const result = await window.registration.submit({
                    directorFullName: this.directorFullName,
                    directorEmail: this.directorEmail,
                    directorPhone: this.directorPhone,
                    directorPassword: this.directorPassword,
                    schoolName: this.schoolName,
                    schoolAddress: this.schoolAddress || null,
                    city: this.city || null,
                    region: this.region || null,
                    // Champ facultatif : chaîne vide -> null, sinon entier.
                    estimatedStudentCount: this.estimatedStudentCount === ''
                        ? null
                        : Number(this.estimatedStudentCount),
                    requestedPlan: this.requestedPlan,
                    website: this.website
                });

                // Succès : on n'affiche plus le formulaire, seulement la référence à conserver. Le mot de
                // passe saisi est abandonné avec le composant — jamais restocké.
                this.trackingReference = result.trackingReference;
            } catch (err) {
                if (err.status === 429) {
                    this.error = 'Trop de demandes envoyées depuis votre connexion. Réessayez dans quelques minutes.';
                } else {
                    // Validation (422) : `details` est un DICTIONNAIRE { "Champ": ["motif"] }
                    // (ExceptionHandlingMiddleware y place le dictionnaire de FluentValidation). On aplatit
                    // les motifs en un message lisible ; sinon on retombe sur le message global.
                    const details = err.details;
                    const reasons = details && typeof details === 'object' && !Array.isArray(details)
                        ? Object.values(details).flat().filter(Boolean)
                        : [];

                    this.error = reasons.length
                        ? reasons.join(' ')
                        : (err.message || 'Envoi impossible. Vérifiez votre réseau et réessayez.');
                }
            } finally {
                this.isSubmitting = false;
            }
        }
    }));
});
