/**
 * Écran Paramètres — regroupe TOUT ce que le Directeur configure pour son établissement :
 *   1. Établissement : identité (nom, adresse, téléphone, logo) — API /schools/current.
 *   2. Configuration : barème, format de date, déconnexion auto, mensualités/an, formats de matricule
 *      — API /schools/current/settings.
 *   3. Années scolaires : géré par schoolYearsView() (js/school-years.js), monté dans l'onglet.
 *
 * L'écriture est réservée au Directeur (l'API répond 403 aux autres). Les autres rôles VOIENT les
 * valeurs — le format de date et le barème pilotent tous les écrans — mais les champs sont en lecture
 * seule et les boutons d'enregistrement masqués. Confort d'affichage : l'API reste seule juge.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('settingsView', () => ({
        // Onglet actif. La redirection depuis l'ancienne route /annees-scolaires arrive avec ?tab=…
        tab: 'etablissement',

        isDirecteur: window.auth.role === 'Directeur',
        isLoading: true,
        loadError: null,

        // --- Établissement (identité) ---
        profile: { name: '', address: '', phone: '', logoUrl: '' },
        profileErrors: {},
        profileSaving: false,
        profileSaved: false,

        // --- Configuration (réglages) ---
        config: {
            gradingScale: '20',
            dateFormat: 'dd/MM/yyyy',
            autoLogoutMinutes: 10,
            tuitionMonthsPerYear: 9,
            studentMatriculeFormat: '',
            teacherMatriculeFormat: ''
        },
        configErrors: {},
        configSaving: false,
        configSaved: false,

        init() {
            const requested = new URLSearchParams(window.location.search).get('tab');
            if (['etablissement', 'configuration', 'annees-scolaires'].includes(requested)) {
                this.tab = requested;
            }
            this.load();
        },

        async load() {
            this.isLoading = true;
            this.loadError = null;
            try {
                const [profile, config] = await Promise.all([
                    window.api.get('/schools/current'),
                    window.api.get('/schools/current/settings')
                ]);

                this.profile = {
                    name: profile.name || '',
                    address: profile.address || '',
                    phone: profile.phone || '',
                    logoUrl: profile.logoUrl || ''
                };
                this.config = {
                    gradingScale: config.gradingScale,
                    dateFormat: config.dateFormat,
                    autoLogoutMinutes: config.autoLogoutMinutes,
                    tuitionMonthsPerYear: config.tuitionMonthsPerYear,
                    studentMatriculeFormat: config.studentMatriculeFormat,
                    teacherMatriculeFormat: config.teacherMatriculeFormat
                };
            } catch (err) {
                this.loadError = err.message || 'Erreur lors du chargement des paramètres.';
            } finally {
                this.isLoading = false;
            }
        },

        // ---------------------------------------------------------------- Établissement

        async saveProfile() {
            this.profileErrors = {};
            this.profileSaved = false;
            this.profileSaving = true;
            try {
                const saved = await window.api.put('/schools/current', {
                    name: this.profile.name,
                    address: this.profile.address || null,
                    phone: this.profile.phone || null,
                    logoUrl: this.profile.logoUrl || null
                });
                this.profile = {
                    name: saved.name || '',
                    address: saved.address || '',
                    phone: saved.phone || '',
                    logoUrl: saved.logoUrl || ''
                };
                this.profileSaved = true;
            } catch (err) {
                this.profileErrors = window.api.toFieldErrors(err, "Enregistrement impossible.");
            } finally {
                this.profileSaving = false;
            }
        },

        // ---------------------------------------------------------------- Configuration

        async saveConfig() {
            this.configErrors = {};
            this.configSaved = false;
            this.configSaving = true;
            try {
                const saved = await window.api.put('/schools/current/settings', {
                    gradingScale: this.config.gradingScale,
                    studentMatriculeFormat: this.config.studentMatriculeFormat,
                    teacherMatriculeFormat: this.config.teacherMatriculeFormat,
                    autoLogoutMinutes: Number(this.config.autoLogoutMinutes),
                    dateFormat: this.config.dateFormat,
                    tuitionMonthsPerYear: Number(this.config.tuitionMonthsPerYear)
                });
                this.config = {
                    gradingScale: saved.gradingScale,
                    dateFormat: saved.dateFormat,
                    autoLogoutMinutes: saved.autoLogoutMinutes,
                    tuitionMonthsPerYear: saved.tuitionMonthsPerYear,
                    studentMatriculeFormat: saved.studentMatriculeFormat,
                    teacherMatriculeFormat: saved.teacherMatriculeFormat
                };
                this.configSaved = true;
            } catch (err) {
                this.configErrors = window.api.toFieldErrors(err, "Enregistrement impossible.");
            } finally {
                this.configSaving = false;
            }
        },

        // ---------------------------------------------------------------- Affichage

        tabClass(name) {
            return this.tab === name
                ? 'border-primary text-primary'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300';
        }
    }));
});
