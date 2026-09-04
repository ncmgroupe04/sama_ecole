/**
 * Modale UNIVERSELLE « Changer mon mot de passe » — montée une seule fois par _Layout
 * (_ChangePasswordModal.cshtml), ouverte depuis la carte de profil de la barre latérale sur
 * n'importe quelle page. Même schéma que la modale « Accès refusé » (access-denied.js) : un store
 * Alpine global plutôt qu'un composant local, pour que le déclencheur (menu du profil) et le
 * contenu (modale) puissent vivre dans deux sous-arbres du DOM distincts.
 *
 * Script CLASSIQUE (pas de "defer" propre, chargé après api.js dont il dépend), donc exécuté avant
 * Alpine différé — l'écouteur alpine:init est enregistré à temps.
 */
document.addEventListener('alpine:init', () => {
    Alpine.store('changePasswordModal', {
        isOpen: false,
        currentPassword: '',
        newPassword: '',
        newPasswordConfirmation: '',
        isSubmitting: false,
        done: false,
        errors: {},

        // Comparaison purement CLIENT : le serveur ne reçoit qu'un seul champ NewPassword
        // (ChangePasswordCommand) — la confirmation n'existe que pour rattraper une faute de frappe
        // avant l'envoi, elle ne fait pas partie du contrat d'API.
        get confirmationMismatch() {
            return this.newPasswordConfirmation.length > 0 && this.newPassword !== this.newPasswordConfirmation;
        },

        open() {
            this.currentPassword = '';
            this.newPassword = '';
            this.newPasswordConfirmation = '';
            this.errors = {};
            this.done = false;
            this.isOpen = true;
        },

        close() {
            this.isOpen = false;
            // Ne jamais laisser une saisie de mot de passe traîner en mémoire une fois la modale
            // fermée (même geste que closeResetPassword() dans users.js).
            this.currentPassword = '';
            this.newPassword = '';
            this.newPasswordConfirmation = '';
        },

        async submit() {
            if (this.confirmationMismatch || this.isSubmitting) return;

            this.isSubmitting = true;
            this.errors = {};

            try {
                await window.api.post('/auth/change-password', {
                    currentPassword: this.currentPassword,
                    newPassword: this.newPassword
                });

                this.currentPassword = '';
                this.newPassword = '';
                this.newPasswordConfirmation = '';
                this.done = true;
            } catch (err) {
                this.errors = window.api.toFieldErrors(err, 'Erreur lors du changement de mot de passe.');
            } finally {
                this.isSubmitting = false;
            }
        }
    });
});
