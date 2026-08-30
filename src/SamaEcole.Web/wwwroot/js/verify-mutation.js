/**
 * Page publique de vérification d'un certificat de mutation (/verifier/mutation/{token}).
 *
 * Un tiers HORS PLATEFORME — l'école d'accueil — scanne le QR imprimé sur le certificat et arrive
 * ici. La page appelle GET /api/v1/state-integration/certificates/verify/{token}, route ANONYME et
 * rate-limitée qui ne révèle AUCUNE donnée de l'élève : seulement le statut (valide / révoqué /
 * inconnu), le numéro, la date, et l'établissement émetteur — de quoi rapprocher le papier présenté.
 *
 * Le token est injecté par la vue (data-token) : il vient du chemin d'URL, jamais d'un champ.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('verifyMutationView', (token) => ({
        token: token || '',
        loading: true,
        result: null,   // { status, certificateNumber, issuedOn, issuingSchoolName, revokedAt }
        failed: false,   // vrai si l'appel lui-même a échoué (réseau, rate-limit) — distinct de « inconnu »

        async init() {
            if (!this.token) {
                this.loading = false;
                this.result = { status: 'unknown' };
                return;
            }

            try {
                this.result = await window.api.get(
                    `/state-integration/certificates/verify/${encodeURIComponent(this.token)}`
                );
            } catch (err) {
                // 429 (trop de tentatives) ou coupure réseau : ce n'est pas « certificat inconnu ».
                this.failed = true;
            } finally {
                this.loading = false;
            }
        },

        get isValid() { return this.result && this.result.status === 'valid'; },
        get isRevoked() { return this.result && this.result.status === 'revoked'; },
        get isUnknown() { return this.result && this.result.status === 'unknown'; },

        formatDate(iso) {
            if (!iso) return '—';
            const d = new Date(iso);
            return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString('fr-FR');
        }
    }));
});
