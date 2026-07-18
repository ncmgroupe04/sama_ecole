/**
 * Onglet Utilisateurs (Paramètres) — gestion du personnel par le Directeur : création de comptes
 * Secrétariat/Finance/Enseignant, blocage/suspension/réactivation (motif obligatoire, JGK-A05),
 * réinitialisation de mot de passe et historique des changements de statut.
 *
 * Le serveur reste seul juge : l'API entière (UsersController) est [Authorize(Roles = Directeur)],
 * les autres rôles ne voient même pas cet onglet (voir Settings/Index.cshtml) — confort d'affichage,
 * pas une protection.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('usersView', () => ({
        users: [],
        isLoading: false,
        error: null,

        isDirecteur: window.auth.role === 'Directeur',

        // --- Création ---
        isCreateOpen: false,
        isSubmitting: false,
        newUser: { fullName: '', email: '', role: 'Secretariat', password: '' },
        createErrors: {},

        // Confirmation « Utilisateur ajouté » affichée après un enregistrement réussi. Rappelle le mot
        // de passe saisi : une fois la modale de création fermée, il n'est visible nulle part ailleurs.
        showAddedDialog: false,
        addedUser: { fullName: '', email: '', password: '' },

        // --- Changement de statut (motif obligatoire, JGK-A05) ---
        statusTarget: null, // { id, fullName, newStatus }
        statusReason: '',
        isChangingStatus: false,
        statusErrors: {},

        // --- Réinitialisation de mot de passe (saisie directe par le Directeur) ---
        passwordTarget: null, // { id, fullName }
        newPassword: '',
        isResettingPassword: false,
        passwordErrors: {},

        // --- Historique des statuts ---
        historyFor: null, // { fullName }
        historyEntries: [],
        isLoadingHistory: false,

        init() {
            if (this.isDirecteur) this.load();
        },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                this.users = await window.api.get('/users');
            } catch (err) {
                this.error = err.message || 'Erreur lors du chargement des utilisateurs.';
            } finally {
                this.isLoading = false;
            }
        },

        roleLabel(role) {
            return {
                Directeur: 'Directeur', Secretariat: 'Secrétariat',
                Finance: 'Finance', Enseignant: 'Enseignant', SuperAdmin: 'Super Admin'
            }[role] || role;
        },

        statusLabel(status) {
            return { Active: 'Actif', Suspended: 'Suspendu', Blocked: 'Bloqué' }[status] || status;
        },

        /** Classes .status-badge-* d'input.css directement (pas le TagHelper <badge> : le statut varie
         *  par ligne côté client, or Variant du TagHelper n'est résolu qu'UNE fois au rendu serveur). */
        statusBadgeVariant(status) {
            return { Active: 'status-badge-success', Suspended: 'status-badge-warning', Blocked: 'status-badge-danger' }[status]
                || 'status-badge-neutral';
        },

        statusActionLabel(newStatus) {
            return { Blocked: 'Bloquer', Suspended: 'Suspendre', Active: 'Réactiver' }[newStatus] || newStatus;
        },

        // ------------------------------------------------------------ Création

        openCreate() {
            this.newUser = { fullName: '', email: '', role: 'Secretariat', password: '' };
            this.createErrors = {};
            this.isCreateOpen = true;
        },

        async submitCreate() {
            this.isSubmitting = true;
            this.createErrors = {};
            try {
                await window.api.post('/users', this.newUser);
                this.isCreateOpen = false;
                this.addedUser = {
                    fullName: this.newUser.fullName,
                    email: this.newUser.email,
                    password: this.newUser.password
                };
                await this.load();
                this.showAddedDialog = true; // confirmation « Utilisateur ajouté »
            } catch (err) {
                this.createErrors = window.api.toFieldErrors(err, 'Erreur lors de la création du compte.');
            } finally {
                this.isSubmitting = false;
            }
        },

        // ------------------------------------------------------------ Statut

        /** newStatus : 'Blocked' | 'Suspended' | 'Active' — le motif se saisit dans la modale (voir _UsersPanel). */
        openStatusChange(user, newStatus) {
            this.statusTarget = { id: user.id, fullName: user.fullName, newStatus };
            this.statusReason = '';
            this.statusErrors = {};
        },

        closeStatusChange() {
            this.statusTarget = null;
        },

        async submitStatusChange() {
            if (!this.statusTarget) return;

            this.isChangingStatus = true;
            this.statusErrors = {};
            try {
                await window.api.patch(`/users/${this.statusTarget.id}/status`, {
                    status: this.statusTarget.newStatus,
                    reason: this.statusReason
                });
                this.closeStatusChange();
                await this.load();
            } catch (err) {
                this.statusErrors = window.api.toFieldErrors(err, 'Erreur lors du changement de statut.');
            } finally {
                this.isChangingStatus = false;
            }
        },

        // ------------------------------------------------------------ Mot de passe

        openResetPassword(user) {
            this.passwordTarget = { id: user.id, fullName: user.fullName };
            this.newPassword = '';
            this.passwordErrors = {};
        },

        closeResetPassword() {
            this.passwordTarget = null;
        },

        async submitResetPassword() {
            if (!this.passwordTarget) return;

            this.isResettingPassword = true;
            this.passwordErrors = {};
            try {
                await window.api.patch(`/users/${this.passwordTarget.id}/password`, {
                    newPassword: this.newPassword
                });
                this.closeResetPassword();
            } catch (err) {
                this.passwordErrors = window.api.toFieldErrors(err, 'Erreur lors de la réinitialisation.');
            } finally {
                this.isResettingPassword = false;
            }
        },

        // ------------------------------------------------------------ Historique

        async openHistory(user) {
            this.historyFor = { fullName: user.fullName };
            this.historyEntries = [];
            this.isLoadingHistory = true;
            try {
                this.historyEntries = await window.api.get(`/users/${user.id}/status-history`);
            } catch (err) {
                this.error = err.message || "Erreur lors du chargement de l'historique.";
                this.historyFor = null;
            } finally {
                this.isLoadingHistory = false;
            }
        },

        closeHistory() {
            this.historyFor = null;
            this.historyEntries = [];
        },

        formatDate(iso) {
            if (!iso) return '';
            const d = new Date(iso);
            return d.toLocaleDateString('fr-FR') + ' ' + d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
        }
    }));
});
