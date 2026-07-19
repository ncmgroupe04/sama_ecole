/**
 * Écran Paramètres — regroupe TOUT ce que le Directeur configure pour son établissement :
 *   1. Établissement : identité (nom, adresse, téléphone, logo) — API /schools/current.
 *   2. Configuration : barème, format de date, déconnexion auto, mensualités/an, formats de matricule
 *      — API /schools/current/settings — et les mentions du bulletin — API /grades/mentions.
 *   3. Années scolaires : géré par schoolYearsView() (js/school-years.js), monté dans l'onglet.
 *
 * L'écriture est réservée au Directeur (l'API répond 403 aux autres). Les autres rôles VOIENT les
 * valeurs — le format de date et le barème pilotent tous les écrans — mais les champs sont en lecture
 * seule et les boutons d'enregistrement masqués. Confort d'affichage : l'API reste seule juge.
 *
 * Exception ticket JGK-G02 (délégation FACULTATIVE, à la guise du Directeur de CHAQUE école — jamais
 * un rôle codé en dur) : le barème et les mentions du bulletin sont ÉCRITS par le Directeur, et par le
 * Secrétariat SEULEMENT si config.allowSecretaryToManageGrading est activé (canManageGradingConfig,
 * un getter — jamais une valeur figée au chargement). Ce booléen vit dans SchoolSettings et n'est
 * modifiable que par le Directeur, via la case à cocher du formulaire bundlé ci-dessous (saveConfig).
 * Le barème a son propre formulaire/bouton (saveGradingScale, PUT /schools/current/settings/grading-scale)
 * séparé du reste de la Configuration (saveConfig, PUT /schools/current/settings) : ce dernier reste
 * Directeur seul, sinon le Secrétariat gagnerait aussi la main sur les formats de matricule, la
 * déconnexion auto et les mensualités — cette même requête PUT est cependant la SEULE à pouvoir
 * changer allowSecretaryToManageGrading, d'où la case à cocher dans CE formulaire précisément.
 *
 * Matrice d'autorisation "Photoshop" : même mécanique pour allowFinanceToModifyFees et
 * allowFinanceToDeleteFees (JGK-F01), lus par fees.js pour afficher/masquer les boutons Modifier et
 * Supprimer de l'écran Finance — deux commutateurs indépendants, écrits par la même requête PUT que
 * la délégation du barème.
 */
document.addEventListener('alpine:init', () => {
    Alpine.data('settingsView', () => ({
        // Onglet actif. La redirection depuis l'ancienne route /annees-scolaires arrive avec ?tab=…
        tab: 'etablissement',

        isDirecteur: window.auth.role === 'Directeur',
        isSecretariat: window.auth.role === 'Secretariat',
        isLoading: true,
        loadError: null,

        // --- Établissement (identité) ---
        profile: {
            name: '', address: '', phone: '', logoUrl: '',
            inspectionAcademie: '', inspectionEducationFormation: '', nomLycee: ''
        },
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
            teacherMatriculeFormat: '',
            allowSecretaryToManageGrading: false,
            allowFinanceToModifyFees: false,
            allowFinanceToDeleteFees: false
        },
        configErrors: {},
        configSaving: false,
        configSaved: false,

        // --- Barème (JGK-G02 : formulaire séparé, voir note en tête de fichier) ---
        // Getter, PAS une valeur figée au chargement : dépend de config.allowSecretaryToManageGrading,
        // que seul le Directeur peut basculer (case à cocher du formulaire Réglages ci-dessous).
        get canManageGradingConfig() {
            return this.isDirecteur || (this.isSecretariat && this.config.allowSecretaryToManageGrading);
        },
        gradingScaleErrors: {},
        gradingScaleSaving: false,
        gradingScaleSaved: false,

        // --- Mentions du bulletin (dans l'onglet Configuration) ---
        canViewMentions: window.auth.role === 'Directeur' || window.auth.role === 'Secretariat' || window.auth.role === 'Enseignant',
        mentions: [],
        isMentionCreateOpen: false,
        mentionSubmitting: false,
        newMention: { label: '', minAverage: null },
        mentionCreateErrors: {},
        showMentionAddedDialog: false,
        addedMentionLabel: '',

        init() {
            const requested = new URLSearchParams(window.location.search).get('tab');
            if (['etablissement', 'configuration', 'annees-scolaires', 'utilisateurs', 'journal-audit', 'facturation'].includes(requested)) {
                this.tab = requested;
            }
            this.load();
        },

        async load() {
            this.isLoading = true;
            this.loadError = null;
            try {
                const requests = [
                    window.api.get('/schools/current'),
                    window.api.get('/schools/current/settings')
                ];
                if (this.canViewMentions) requests.push(window.api.get('/grades/mentions'));

                const [profile, config, mentions] = await Promise.all(requests);
                if (this.canViewMentions) this.mentions = mentions;

                this.profile = {
                    name: profile.name || '',
                    address: profile.address || '',
                    phone: profile.phone || '',
                    logoUrl: profile.logoUrl || '',
                    inspectionAcademie: profile.inspectionAcademie || '',
                    inspectionEducationFormation: profile.inspectionEducationFormation || '',
                    nomLycee: profile.nomLycee || ''
                };
                this.config = {
                    gradingScale: config.gradingScale,
                    dateFormat: config.dateFormat,
                    autoLogoutMinutes: config.autoLogoutMinutes,
                    tuitionMonthsPerYear: config.tuitionMonthsPerYear,
                    studentMatriculeFormat: config.studentMatriculeFormat,
                    teacherMatriculeFormat: config.teacherMatriculeFormat,
                    allowSecretaryToManageGrading: config.allowSecretaryToManageGrading,
                    allowFinanceToModifyFees: config.allowFinanceToModifyFees,
                    allowFinanceToDeleteFees: config.allowFinanceToDeleteFees
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
                    logoUrl: this.profile.logoUrl || null,
                    inspectionAcademie: this.profile.inspectionAcademie || null,
                    inspectionEducationFormation: this.profile.inspectionEducationFormation || null,
                    nomLycee: this.profile.nomLycee || null
                });
                this.profile = {
                    name: saved.name || '',
                    address: saved.address || '',
                    phone: saved.phone || '',
                    logoUrl: saved.logoUrl || '',
                    inspectionAcademie: saved.inspectionAcademie || '',
                    inspectionEducationFormation: saved.inspectionEducationFormation || '',
                    nomLycee: saved.nomLycee || ''
                };
                this.profileSaved = true;
            } catch (err) {
                this.profileErrors = window.api.toFieldErrors(err, "Enregistrement impossible.");
            } finally {
                this.profileSaving = false;
            }
        },

        // ---------------------------------------------------------------- Configuration

        // showConfirmation=false pour les 3 commutateurs de délégation (Configuration) : une bascule
        // en un clic n'a pas besoin d'une boîte de dialogue à fermer soi-même, contrairement au
        // formulaire « Réglages de l'établissement », validé par un vrai bouton Enregistrer.
        async saveConfig(showConfirmation = true) {
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
                    tuitionMonthsPerYear: Number(this.config.tuitionMonthsPerYear),
                    allowSecretaryToManageGrading: this.config.allowSecretaryToManageGrading,
                    allowFinanceToModifyFees: this.config.allowFinanceToModifyFees,
                    allowFinanceToDeleteFees: this.config.allowFinanceToDeleteFees
                });
                this.config = {
                    gradingScale: saved.gradingScale,
                    dateFormat: saved.dateFormat,
                    autoLogoutMinutes: saved.autoLogoutMinutes,
                    tuitionMonthsPerYear: saved.tuitionMonthsPerYear,
                    studentMatriculeFormat: saved.studentMatriculeFormat,
                    teacherMatriculeFormat: saved.teacherMatriculeFormat,
                    allowSecretaryToManageGrading: saved.allowSecretaryToManageGrading,
                    allowFinanceToModifyFees: saved.allowFinanceToModifyFees,
                    allowFinanceToDeleteFees: saved.allowFinanceToDeleteFees
                };
                this.configSaved = showConfirmation;
            } catch (err) {
                this.configErrors = window.api.toFieldErrors(err, "Enregistrement impossible.");
            } finally {
                this.configSaving = false;
            }
        },

        // ---------------------------------------------------------------- Barème (JGK-G02)

        async saveGradingScale() {
            this.gradingScaleErrors = {};
            this.gradingScaleSaved = false;
            this.gradingScaleSaving = true;
            try {
                const saved = await window.api.put('/schools/current/settings/grading-scale', {
                    gradingScale: this.config.gradingScale
                });
                this.config.gradingScale = saved.gradingScale;
                this.gradingScaleSaved = true;
            } catch (err) {
                this.gradingScaleErrors = window.api.toFieldErrors(err, "Enregistrement impossible.");
            } finally {
                this.gradingScaleSaving = false;
            }
        },

        // ---------------------------------------------------------------- Mentions du bulletin

        /** Vrai tant que l'école n'a créé aucune mention : GetMentionsQueryHandler renvoie alors le
         *  barème par défaut, avec des id null (voir CreateMentionCommand). Dès le premier ajout, ce
         *  barème par défaut disparaît entièrement — le Directeur doit recréer tout ce qu'il veut garder. */
        get isDefaultMentions() {
            return this.mentions.length > 0 && this.mentions.every((m) => !m.id);
        },

        openCreateMention() {
            this.newMention = { label: '', minAverage: null };
            this.mentionCreateErrors = {};
            this.isMentionCreateOpen = true;
        },

        async submitCreateMention() {
            this.mentionSubmitting = true;
            this.mentionCreateErrors = {};
            try {
                await window.api.post('/grades/mentions', this.newMention);
                this.isMentionCreateOpen = false;
                this.addedMentionLabel = this.newMention.label;
                this.mentions = await window.api.get('/grades/mentions');
                this.showMentionAddedDialog = true;
            } catch (err) {
                this.mentionCreateErrors = window.api.toFieldErrors(err, 'Erreur lors de la création de la mention.');
            } finally {
                this.mentionSubmitting = false;
            }
        },

        // Même contournement du sérialiseur decimal que Subjects.formatCoefficient (16.00 → 16).
        formatAverage(value) {
            return Number(value).toLocaleString('fr-FR');
        },

        // ---------------------------------------------------------------- Affichage

        tabClass(name) {
            return this.tab === name
                ? 'border-primary text-primary'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300';
        }
    }));
});
