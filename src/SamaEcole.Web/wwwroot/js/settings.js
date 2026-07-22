/**
 * Écran Paramètres — regroupe TOUT ce que le Directeur configure pour son établissement :
 *   1. Établissement : identité (nom, adresse, téléphone, logo) — API /schools/current.
 *   2. Configuration : format de date, déconnexion auto, mensualités/an, formats de matricule
 *      — API /schools/current/settings — et les mentions du bulletin — API /grades/mentions.
 *   3. Années scolaires : géré par schoolYearsView() (js/school-years.js), monté dans l'onglet.
 *
 * L'écriture est réservée au Directeur (l'API répond 403 aux autres). Les autres rôles VOIENT les
 * valeurs — le format de date pilote tous les écrans — mais les champs sont en lecture seule et les
 * boutons d'enregistrement masqués. Confort d'affichage : l'API reste seule juge.
 *
 * BARÈME DE NOTATION : ce n'est plus un réglage. Il découle du CYCLE de la classe
 * (GradingScaleGuard.ScaleForCycle : Primaire /10, Collège & Lycée /20) et s'applique à la saisie des
 * notes, à l'import, à la fiche élève et au bulletin PDF. Le sélecteur qui vivait dans cet écran a été
 * retiré au profit d'une note explicative : il laissait croire qu'un réglage global pilotait les notes.
 * config.gradingScale est TOUJOURS chargé et renvoyé tel quel par saveConfig — il sert encore de plafond
 * aux seuils de mentions côté serveur (CreateMentionCommandHandler + MentionScale), d'où son usage
 * résiduel dans le panneau Mentions. Ne pas le supprimer de l'état sans corriger cette validation.
 *
 * Ticket JGK-G02 (délégation FACULTATIVE, à la guise du Directeur de CHAQUE école — jamais un rôle
 * codé en dur) : les mentions du bulletin sont ÉCRITES par le Directeur, et par le Secrétariat
 * SEULEMENT si config.allowSecretaryToManageGrading est activé (canManageGradingConfig, un getter —
 * jamais une valeur figée au chargement). Ce booléen vit dans SchoolSettings et n'est modifiable que
 * par le Directeur, via la case à cocher du formulaire bundlé ci-dessous (saveConfig) : cette requête
 * PUT reste Directeur seul, sinon le Secrétariat gagnerait aussi la main sur les formats de matricule,
 * la déconnexion auto et les mensualités — et c'est pourtant la SEULE à pouvoir changer
 * allowSecretaryToManageGrading, d'où la case à cocher dans CE formulaire précisément.
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
            inspectionAcademie: '', inspectionEducationFormation: '', nomLycee: '',
            // Coordonnées et mentions légales de l'en-tête du reçu (NINEA / RCCM).
            email: '', ninea: '', registreCommerce: ''
        },
        profileErrors: {},
        profileSaving: false,
        profileSaved: false,
        isUploadingLogo: false,
        logoUploadError: null,

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

        // --- Délégation de la configuration des notes (JGK-G02) ---
        // Getter, PAS une valeur figée au chargement : dépend de config.allowSecretaryToManageGrading,
        // que seul le Directeur peut basculer (case à cocher du formulaire Réglages ci-dessous).
        // Ne gouverne plus que les mentions du bulletin (le barème n'est plus un réglage, voir en tête).
        get canManageGradingConfig() {
            return this.isDirecteur || (this.isSecretariat && this.config.allowSecretaryToManageGrading);
        },

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
                    nomLycee: profile.nomLycee || '',
                    email: profile.email || '',
                    ninea: profile.ninea || '',
                    registreCommerce: profile.registreCommerce || ''
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
                    nomLycee: this.profile.nomLycee || null,
                    email: this.profile.email || null,
                    ninea: this.profile.ninea || null,
                    registreCommerce: this.profile.registreCommerce || null
                });
                this.profile = {
                    name: saved.name || '',
                    address: saved.address || '',
                    phone: saved.phone || '',
                    logoUrl: saved.logoUrl || '',
                    inspectionAcademie: saved.inspectionAcademie || '',
                    inspectionEducationFormation: saved.inspectionEducationFormation || '',
                    nomLycee: saved.nomLycee || '',
                    email: saved.email || '',
                    ninea: saved.ninea || '',
                    registreCommerce: saved.registreCommerce || ''
                };
                this.profileSaved = true;
            } catch (err) {
                this.profileErrors = window.api.toFieldErrors(err, "Enregistrement impossible.");
            } finally {
                this.profileSaving = false;
            }
        },

        async uploadLogoFile(event) {
            const file = event.target.files && event.target.files[0];
            if (!file) return;

            this.logoUploadError = null;
            if (file.size > 2 * 1024 * 1024) {
                this.logoUploadError = 'Le fichier dépasse la taille maximale autorisée (2 Mo).';
                event.target.value = '';
                return;
            }

            const allowedTypes = ['image/png', 'image/jpeg', 'image/webp'];
            if (!allowedTypes.includes(file.type)) {
                this.logoUploadError = 'Format non supporté. Seuls PNG, JPEG et WEBP sont autorisés.';
                event.target.value = '';
                return;
            }

            const formData = new FormData();
            formData.append('file', file);

            this.isUploadingLogo = true;
            try {
                const data = await window.api.upload('/schools/current/logo', formData);
                if (data && data.url) {
                    this.profile.logoUrl = data.url;
                }
            } catch (err) {
                this.logoUploadError = err.message || 'Erreur lors de l\'envoi du fichier.';
            } finally {
                this.isUploadingLogo = false;
                event.target.value = '';
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

        // Segmented control (Views/Settings/Index.cshtml) : pastille blanche + texte primaire pour
        // l'onglet actif, fond transparent + texte discret (éclairci au survol) pour les autres.
        tabClass(name) {
            return this.tab === name
                ? 'bg-white text-primary font-semibold shadow-sm'
                : 'text-slate-600 hover:bg-white/60 hover:text-slate-900';
        }
    }));
});
