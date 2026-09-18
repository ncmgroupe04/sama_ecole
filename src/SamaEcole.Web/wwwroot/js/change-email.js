/**
 * Modale UNIVERSELLE « Changer mon e-mail » — montée une seule fois par _Layout / _SuperAdminLayout
 * (_ChangeEmailModal.cshtml / _ChangeEmailModalDark.cshtml), ouverte depuis la carte de profil de la
 * barre latérale sur n'importe quelle page. Même schéma que change-password.js : un store Alpine
 * global, script classique (pas de "defer") chargé après api.js et avant Alpine différé.
 */
document.addEventListener('alpine:init', () => {
    Alpine.store('changeEmailModal', {
        isOpen: false,
        newEmail: '',
        currentPassword: '',
        isSubmitting: false,
        done: false,
        errors: {},

        open() {
            this.newEmail = '';
            this.currentPassword = '';
            this.errors = {};
            this.done = false;
            this.isOpen = true;
        },

        close() {
            this.isOpen = false;
            // Ne jamais laisser une saisie de mot de passe traîner en mémoire une fois la modale
            // fermée (même geste que closeResetPassword() dans users.js et close() dans change-password.js).
            this.newEmail = '';
            this.currentPassword = '';
        },

        async submit() {
            if (this.isSubmitting) return;

            this.isSubmitting = true;
            this.errors = {};

            try {
                await window.api.post('/auth/change-email', {
                    newEmail: this.newEmail,
                    currentPassword: this.currentPassword
                });

                this.newEmail = '';
                this.currentPassword = '';
                this.done = true;
            } catch (err) {
                // Un e-mail déjà pris remonte en 409 (DuplicateRecordException) sans détail par champ
                // (voir ExceptionHandlingMiddleware) : toFieldErrors() le range alors dans errors.global,
                // affiché par le bandeau d'erreur de la modale — pas un champ précis.
                this.errors = window.api.toFieldErrors(err, "Erreur lors du changement d'e-mail.");
            } finally {
                this.isSubmitting = false;
            }
        }
    });
});
