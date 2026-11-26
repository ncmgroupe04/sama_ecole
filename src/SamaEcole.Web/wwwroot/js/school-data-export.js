/**
 * « Exporter mes données » (Paramètres › Paramètres système) — télécharge l'archive ZIP de toutes les données de l'établissement
 * (POST /api/v1/exports/school-data). Directeur seul ; le serveur exige son mot de passe (422 sur le champ si refusé) et journalise
 * chaque export. Aucune règle ici : l'écran relaie le mot de passe et enregistre le fichier reçu.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('schoolDataExport', () => ({
        canExport: window.auth.role === 'Directeur',

        password: '',
        isExporting: false,
        error: null,
        passwordError: null,
        notice: null,

        async startExport() {
            if (this.isExporting) return;
            this.error = null;
            this.passwordError = null;
            this.notice = null;

            if (!this.password) {
                this.passwordError = 'Saisissez votre mot de passe pour confirmer.';
                return;
            }

            this.isExporting = true;
            try {
                if (window.auth.isAuthenticated() && window.auth.isAccessTokenStale()) {
                    await window.api.refreshOrRedirect();
                }

                const response = await fetch('/api/v1/exports/school-data', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        Authorization: `Bearer ${window.auth.accessToken}`
                    },
                    credentials: 'same-origin',
                    body: JSON.stringify({ password: this.password })
                });

                if (!response.ok) {
                    await this.handleFailure(response);
                    return;
                }

                const blob = await response.blob();
                this.save(blob, this.fileName(response));
                this.password = '';
                this.notice = 'Export terminé : le fichier a été téléchargé. Conservez-le en lieu sûr, il contient les données personnelles de vos élèves.';
            } catch {
                this.error = 'Connexion interrompue pendant l\'export. Réessayez quand le réseau est rétabli.';
            } finally {
                this.isExporting = false;
            }
        },

        /** 422 = mot de passe refusé (sous le champ) ; 429 = limite horaire ; 403 = rôle ; le reste = message générique. */
        async handleFailure(response) {
            if (response.status === 422) {
                this.passwordError = 'Mot de passe incorrect.';
                return;
            }
            if (response.status === 429) {
                this.error = 'Trop d\'exports récents : 3 par heure au maximum. Réessayez plus tard.';
                return;
            }
            if (response.status === 403) {
                this.error = 'L\'export des données est réservé au Directeur.';
                return;
            }
            this.error = 'L\'export a échoué. Réessayez ; si le problème persiste, contactez le support.';
        },

        /** Nom proposé par le serveur (Content-Disposition), sinon un nom daté. */
        fileName(response) {
            const disposition = response.headers.get('Content-Disposition') || '';
            const match = disposition.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
            if (match) return decodeURIComponent(match[1]);
            return `export-donnees-${new Date().toISOString().slice(0, 10)}.zip`;
        },

        save(blob, name) {
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = name;
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        }
    }));
});
