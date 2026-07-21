/**
 * Réinitialisation de mot de passe self-service — écrans /mot-de-passe-oublie et
 * /reinitialiser-mot-de-passe.
 *
 * Pages entièrement ANONYMES : aucune session, aucun jeton d'accès. Le jeton reçu par e-mail fait
 * office d'authentification pour la seule opération de changement de mot de passe.
 */
(() => {
    'use strict';

    const API_BASE = '/api/v1';

    /**
     * Extrait le message d'erreur du format normalisé (docs/Volume_4_API_Design.md §0.4). Les erreurs
     * de validation portent le détail par champ ; on retient le premier, l'écran n'ayant qu'un champ.
     */
    async function toMessage(response) {
        const payload = await response.json().catch(() => null);

        if (payload && payload.errors) {
            const first = Object.values(payload.errors).flat()[0];
            if (first) return first;
        }

        if (payload && payload.message) return payload.message;

        if (response.status === 429) {
            return 'Trop de demandes. Patientez quelques minutes avant de réessayer.';
        }

        return `Erreur inattendue (${response.status}).`;
    }

    window.passwordReset = {
        async requestLink(email) {
            const response = await fetch(`${API_BASE}/auth/forgot-password`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email })
            });

            // 202 quelle que soit l'existence du compte : l'écran ne peut donc RIEN en déduire, et
            // c'est voulu (anti-énumération de comptes — voir ForgotPasswordCommandHandler).
            if (!response.ok) {
                throw new Error(await toMessage(response));
            }
        },

        async apply(token, newPassword) {
            const response = await fetch(`${API_BASE}/auth/reset-password`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ token, newPassword })
            });

            if (!response.ok) {
                throw new Error(await toMessage(response));
            }

            return await response.json();
        }
    };
})();

document.addEventListener('alpine:init', () => {
    Alpine.data('forgotPasswordForm', () => ({
        email: '',
        isSubmitting: false,
        sent: false,
        error: null,

        async submit() {
            this.error = null;

            if (!this.email.trim()) {
                this.error = "L'adresse e-mail est obligatoire.";
                return;
            }

            this.isSubmitting = true;

            try {
                await window.passwordReset.requestLink(this.email.trim());
                this.sent = true;
            } catch (e) {
                this.error = e.message;
            } finally {
                this.isSubmitting = false;
            }
        }
    }));

    Alpine.data('resetPasswordForm', () => ({
        // Le jeton n'est lu QUE depuis l'URL : il n'est ni saisi, ni stocké, ni renvoyé ailleurs.
        token: new URLSearchParams(window.location.search).get('token') || '',
        newPassword: '',
        isSubmitting: false,
        done: false,
        error: null,

        init() {
            // Lien tronqué par un client de messagerie : le dire tout de suite plutôt que de laisser
            // l'utilisateur saisir un mot de passe pour rien.
            if (!this.token) {
                this.error = 'Lien de réinitialisation incomplet. Refaites une demande depuis la page de connexion.';
            }
        },

        async submit() {
            this.error = null;

            if (!this.newPassword) {
                this.error = 'Le nouveau mot de passe est obligatoire.';
                return;
            }

            this.isSubmitting = true;

            try {
                await window.passwordReset.apply(this.token, this.newPassword);
                this.done = true;
            } catch (e) {
                this.error = e.message;
            } finally {
                this.isSubmitting = false;
            }
        }
    }));
});
