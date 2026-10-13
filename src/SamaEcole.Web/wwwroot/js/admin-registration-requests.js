/**
 * Écran Super Admin — revue des demandes d'inscription self-service (ticket JGK-I03).
 *
 * Lister, filtrer par statut, puis APPROUVER (crée établissement + Directeur + abonnement en une
 * transaction atomique côté serveur) ou REJETER (motif obligatoire).
 *
 * Le serveur reste seul juge : AdminRegistrationRequestsController est [Authorize(Roles = SuperAdmin)].
 * Le garde-fou de rôle ci-dessous n'est qu'un confort d'affichage.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('registrationRequestsView', () => ({
        requests: [],
        statusFilter: 'Pending',
        isLoading: false,
        error: null,

        isSuperAdmin: window.auth.role === 'SuperAdmin',

        // --- Approbation (action lourde : confirmation explicite) ---
        approveTarget: null, // l'objet demande
        isApproving: false,
        approveError: null,

        // --- Rejet (motif obligatoire) ---
        rejectTarget: null,
        rejectReason: '',
        isRejecting: false,
        rejectErrors: {},

        // --- Confirmations post-action (approbation / rejet) ---
        approvedResult: null, // { schoolName, directorFullName }
        rejectedResult: null, // { schoolName }

        // --- Détails ---
        detailTarget: null,

        init() {
            if (this.isSuperAdmin) this.load();
        },

        async load() {
            this.isLoading = true;
            this.error = null;
            try {
                // 'All' = pas de filtre serveur ; sinon on transmet le statut choisi.
                const query = this.statusFilter === 'All' ? '' : `?status=${this.statusFilter}`;
                this.requests = await window.api.get(`/admin/registration-requests${query}`);
            } catch (err) {
                this.error = window.api.toMessage(err, 'Erreur lors du chargement des demandes.');
            } finally {
                this.isLoading = false;
            }
        },

        get pendingCount() {
            return this.requests.filter(r => r.status === 'Pending').length;
        },

        statusLabel(status) {
            return { Pending: 'En attente', Approved: 'Approuvée', Rejected: 'Rejetée' }[status] || status;
        },

        /**
         * Classes de la pastille de statut. Renvoie les classes EN DUR plutôt qu'un nom de variante
         * (.status-badge-warning & consorts) : ces variantes partagées sont définies en thème CLAIR,
         * et cet écran vit désormais dans la console Super Admin, toujours sombre. Même forme que
         * statusClasses() de superadmin-billing.js et superadmin-schools.js.
         */
        statusClasses(status) {
            return {
                Pending: 'bg-amber-500/15 text-amber-400 ring-1 ring-inset ring-amber-500/20',
                Approved: 'bg-emerald-500/15 text-emerald-400 ring-1 ring-inset ring-emerald-500/20',
                Rejected: 'bg-rose-500/15 text-rose-400 ring-1 ring-inset ring-rose-500/20'
            }[status] || 'bg-zinc-800 text-zinc-300';
        },

        /**
         * « Privé · Bicycle · 400 à 800 élèves » / « Public · Collège (CEM) · sur devis » : l'offre déclarée à
         * l'inscription, dans les termes de la grille de la vitrine. Le plan d'abonnement en est DÉDUIT côté
         * serveur (il ne se choisit plus) et n'est donc plus affiché comme un choix du demandeur.
         */
        offerLabel(request) {
            // Offre par tranche d'effectif : profil, tranche, internat et cycles cochés (le profil de cycles de la grille
            // historique, déduit côté serveur, ne dit plus rien d'utile ici).
            if (request.profile && request.tier) return this.tieredOfferLabel(request);

            const isPublic = request.ownership === 'Public';
            const cycles = {
                Primaire: isPublic ? 'École élémentaire' : 'Primaire',
                College: isPublic ? 'Collège (CEM)' : 'Collège',
                Lycee: 'Lycée',
                Bicycle: 'Bicycle',
                Complexe: 'Grand complexe'
            }[request.cycleProfile] || request.cycleProfile;

            if (isPublic) return `Public · ${cycles} · sur devis`;

            const sizes = request.cycleProfile === 'Bicycle'
                ? { Small: 'moins de 400 élèves', Medium: '400 à 800 élèves', Large: 'plus de 800 élèves' }
                : request.cycleProfile === 'Complexe'
                    ? { Small: 'moins de 500 élèves', Medium: '500 à 1 000 élèves', Large: 'plus de 1 000 élèves' }
                    : { Small: 'petit', Medium: 'moyen', Large: 'grand' };

            return `Privé · ${cycles}${request.sizeTier ? ' · ' + sizes[request.sizeTier] : ''}`;
        },

        /** « Privé · Franco-Arabe · jusqu'à 150 élèves · Internat · Primaire, Collège ». */
        tieredOfferLabel(request) {
            const profiles = {
                EnseignementGeneral: 'Enseignement général',
                FrancoArabe: 'Franco-Arabe',
                Elementaire: 'Élémentaire',
                InternatDaara: 'Daara moderne (internat)',
                DaaraMemorisation: 'Daara — Mémorisation',
                ComptabiliteRapports: 'Comptabilité & rapports'
            };
            const tiers = {
                Tier_100: "jusqu'à 100 élèves",
                Tier1_150: "jusqu'à 150 élèves",
                Tier_250: "jusqu'à 250 élèves",
                Tier2_400: "jusqu'à 400 élèves",
                Tier3_800: "jusqu'à 800 élèves (sur devis)",
                Tier4_Custom: 'sur mesure (sur devis)'
            };
            const cycleNames = { Maternelle: 'Préscolaire', Primaire: 'Élémentaire', College: 'Moyen', Lycee: 'Secondaire' };
            const cycles = (request.cycles || '').split(',').filter(Boolean).map((c) => cycleNames[c] || c).join(', ');

            return ['Privé', profiles[request.profile] || request.profile, tiers[request.tier] || request.tier]
                .concat(request.hasBoarding ? ['Internat'] : [], cycles ? [cycles] : [])
                .join(' · ');
        },

        formatDate(iso) {
            if (!iso) return '';
            return new Date(iso).toLocaleDateString('fr-FR');
        },

        // ------------------------------------------------------------ Détails

        openDetail(request) {
            this.detailTarget = request;
        },

        closeDetail() {
            this.detailTarget = null;
        },

        // ------------------------------------------------------------ Approbation

        openApprove(request) {
            this.approveTarget = request;
            this.approveError = null;
        },

        closeApprove() {
            this.approveTarget = null;
        },

        async submitApprove() {
            if (!this.approveTarget) return;

            this.isApproving = true;
            this.approveError = null;
            try {
                const { schoolName, directorFullName } = this.approveTarget;
                const result = await window.api.post(`/admin/registration-requests/${this.approveTarget.id}/approve`);
                this.closeApprove();
                this.closeDetail();
                await this.load();
                // École/compte/abonnement sont créés même si l'e-mail de confirmation échoue (ex.
                // aucun SMTP configuré sur ce déploiement) : emailSent le signale, pour prévenir le
                // Directeur par un autre canal si besoin — voir ApproveRegistrationRequestHandler.
                this.approvedResult = { schoolName, directorFullName, emailSent: result.emailSent };
            } catch (err) {
                this.approveError = window.api.toMessage(err, "Erreur lors de l'approbation.");
            } finally {
                this.isApproving = false;
            }
        },

        // ------------------------------------------------------------ Rejet

        openReject(request) {
            this.rejectTarget = request;
            this.rejectReason = '';
            this.rejectErrors = {};
        },

        closeReject() {
            this.rejectTarget = null;
        },

        async submitReject() {
            if (!this.rejectTarget) return;

            this.isRejecting = true;
            this.rejectErrors = {};
            try {
                const { schoolName } = this.rejectTarget;
                await window.api.post(`/admin/registration-requests/${this.rejectTarget.id}/reject`, {
                    reason: this.rejectReason
                });
                this.closeReject();
                this.closeDetail();
                await this.load();
                this.rejectedResult = { schoolName }; // confirmation « Demande rejetée »
            } catch (err) {
                this.rejectErrors = window.api.toFieldErrors(err, 'Erreur lors du rejet.');
            } finally {
                this.isRejecting = false;
            }
        }
    }));
});
