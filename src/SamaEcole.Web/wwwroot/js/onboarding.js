/*
 * Assistant d'Onboarding (Setup Wizard, Ticket 2) — écran /onboarding.
 *
 * Le clic sur une carte applique IMMÉDIATEMENT le profil (POST
 * /schools/current/settings/establishment-profile) : même patron que le bascule Privé/Public de
 * Paramètres (settings.js › saveConfig), pas d'étape de confirmation séparée — choisir un profil
 * n'est pas une action destructive, et reste modifiable plus tard (revenir sur cet écran, ou la
 * future section dédiée de Paramètres).
 *
 * Après application réussie, redirection vers l'atterrissage habituel du Directeur
 * (window.auth.defaultLandingForRole()) : c'est LÀ que setup-assistant.js prend le relais pour la
 * checklist de configuration détaillée (année scolaire, frais, enseignants…).
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('onboardingWizard', () => ({
        submittingProfile: null,
        error: null,

        get isSubmitting() {
            return this.submittingProfile !== null;
        },

        async choose(profile) {
            if (this.isSubmitting) return;

            this.error = null;
            this.submittingProfile = profile;

            try {
                await window.api.post('/schools/current/settings/establishment-profile', { profile });
                window.location.assign(window.auth.defaultLandingForRole());
            } catch (err) {
                this.error = (err && err.message) || "Impossible d'enregistrer ce profil. Réessayez.";
                this.submittingProfile = null;
            }
        }
    }));
});
