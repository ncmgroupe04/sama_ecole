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
            email: '', ninea: '', registreCommerce: '',
            // Intégration étatique (SIMEN, JGK-M05). GPS en deux champs numériques ; « lat, lon »
            // calculé côté serveur (gpsCoordinates) pour l'affichage seul.
            nationalSchoolCode: '', ministryAuthorizationNumber: '', schoolDistrictCode: '',
            gpsLatitude: '', gpsLongitude: '', gpsCoordinates: ''
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
            allowFinanceToDeleteFees: false,
            directorSignatureUrl: '',
            secretarySignatureUrl: '',
            cashierSignatureUrl: '',
            officialStampUrl: '',
            surveillantSignatureUrl: '',
            // TypeEtablissement : Prive (défaut, module Finance actif) ou Public (module Finance masqué).
            typeEtablissement: 'Prive'
        },
        configErrors: {},
        configSaving: false,
        configSaved: false,
        isUploadingDirectorSignature: false,
        directorSignatureUploadError: null,
        isUploadingSecretarySignature: false,
        secretarySignatureUploadError: null,
        isUploadingCashierSignature: false,
        cashierSignatureUploadError: null,
        isUploadingOfficialStamp: false,
        officialStampUploadError: null,
        isUploadingSurveillantSignature: false,
        surveillantSignatureUploadError: null,

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

        // Édition/suppression (correction d'une erreur de saisie sans repasser par toute la liste).
        editingMention: null,
        mentionEditErrors: {},
        isSavingMentionEdit: false,
        deletingMention: null,
        deleteMentionError: null,
        isDeletingMention: false,

        // --- Zone de danger : réinitialisation des données de l'établissement ---
        // Le bouton final reste inerte tant que resetConfirmationMatches est faux. C'est un confort :
        // POST /schools/current/reset-data revérifie EXACTEMENT la même garde côté serveur, car un
        // appel direct (curl, script) ne passe jamais par cette modale.
        isResetSchoolOpen: false,
        resetConfirmation: '',
        resetError: null,
        isResetting: false,
        resetSummary: null,

        // « PURGER » est comparé à la casse — comme côté serveur (ResetSchoolDataConfirmation.Keyword) :
        // c'est le geste délibéré qui fait la valeur de la garde. Le nom de l'école, lui, tolère la
        // casse et les espaces de bord : le Directeur le recopie, il n'a pas à en refaire la graphie.
        get resetConfirmationMatches() {
            const typed = (this.resetConfirmation || '').trim();
            if (!typed) return false;

            const schoolName = (this.profile.name || '').trim();
            return typed === 'PURGER'
                || (schoolName !== '' && typed.toLocaleLowerCase() === schoolName.toLocaleLowerCase());
        },

        // Le compte rendu du serveur liste TOUTES les tables, y compris celles à 0 ligne (utile au
        // diagnostic) ; l'écran, lui, n'affiche que ce qui a réellement bougé.
        get resetEntriesWithRows() {
            if (!this.resetSummary || !this.resetSummary.entries) return [];
            return this.resetSummary.entries.filter(entry => entry.rowsDeleted > 0);
        },

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

                this.profile = this.toProfileState(profile);
                this.config = {
                    gradingScale: config.gradingScale,
                    dateFormat: config.dateFormat,
                    autoLogoutMinutes: config.autoLogoutMinutes,
                    tuitionMonthsPerYear: config.tuitionMonthsPerYear,
                    studentMatriculeFormat: config.studentMatriculeFormat,
                    teacherMatriculeFormat: config.teacherMatriculeFormat,
                    allowSecretaryToManageGrading: config.allowSecretaryToManageGrading,
                    allowFinanceToModifyFees: config.allowFinanceToModifyFees,
                    allowFinanceToDeleteFees: config.allowFinanceToDeleteFees,
                    directorSignatureUrl: config.directorSignatureUrl || '',
                    secretarySignatureUrl: config.secretarySignatureUrl || '',
                    cashierSignatureUrl: config.cashierSignatureUrl || '',
                    officialStampUrl: config.officialStampUrl || '',
                    surveillantSignatureUrl: config.surveillantSignatureUrl || '',
                    typeEtablissement: config.typeEtablissement || 'Prive'
                };
            } catch (err) {
                this.loadError = window.api.toMessage(err, 'Erreur lors du chargement des paramètres.');
            } finally {
                this.isLoading = false;
            }
        },

        // ---------------------------------------------------------------- Établissement

        /** Hydrate l'état du formulaire depuis un SchoolProfileDto (chargement + après enregistrement). */
        toProfileState(dto) {
            return {
                name: dto.name || '',
                address: dto.address || '',
                phone: dto.phone || '',
                logoUrl: dto.logoUrl || '',
                inspectionAcademie: dto.inspectionAcademie || '',
                inspectionEducationFormation: dto.inspectionEducationFormation || '',
                nomLycee: dto.nomLycee || '',
                email: dto.email || '',
                ninea: dto.ninea || '',
                registreCommerce: dto.registreCommerce || '',
                nationalSchoolCode: dto.nationalSchoolCode || '',
                ministryAuthorizationNumber: dto.ministryAuthorizationNumber || '',
                schoolDistrictCode: dto.schoolDistrictCode || '',
                // Number|null -> chaîne pour les <input type="number"> ; '' quand non renseigné.
                gpsLatitude: dto.gpsLatitude === null || dto.gpsLatitude === undefined ? '' : String(dto.gpsLatitude),
                gpsLongitude: dto.gpsLongitude === null || dto.gpsLongitude === undefined ? '' : String(dto.gpsLongitude),
                gpsCoordinates: dto.gpsCoordinates || ''
            };
        },

        async saveProfile() {
            this.profileErrors = {};
            this.profileSaved = false;
            this.profileSaving = true;
            try {
                // GPS : '' -> null (les deux ensemble ou aucune, le serveur revalide) ; sinon Number.
                const lat = this.profile.gpsLatitude === '' ? null : Number(this.profile.gpsLatitude);
                const lon = this.profile.gpsLongitude === '' ? null : Number(this.profile.gpsLongitude);

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
                    registreCommerce: this.profile.registreCommerce || null,
                    nationalSchoolCode: this.profile.nationalSchoolCode || null,
                    ministryAuthorizationNumber: this.profile.ministryAuthorizationNumber || null,
                    schoolDistrictCode: this.profile.schoolDistrictCode || null,
                    gpsLatitude: Number.isFinite(lat) ? lat : null,
                    gpsLongitude: Number.isFinite(lon) ? lon : null
                });
                this.profile = this.toProfileState(saved);
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
                this.logoUploadError = window.api.toMessage(err, 'Erreur lors de l\'envoi du fichier.');
            } finally {
                this.isUploadingLogo = false;
                event.target.value = '';
            }
        },

        async uploadDirectorSignatureFile(event) {
            const file = event.target.files && event.target.files[0];
            if (!file) return;

            this.directorSignatureUploadError = null;
            if (file.size > 2 * 1024 * 1024) {
                this.directorSignatureUploadError = 'Le fichier dépasse la taille maximale autorisée (2 Mo).';
                event.target.value = '';
                return;
            }

            const allowedTypes = ['image/png', 'image/jpeg', 'image/webp'];
            if (!allowedTypes.includes(file.type)) {
                this.directorSignatureUploadError = 'Format non supporté. Seuls PNG, JPEG et WEBP sont autorisés.';
                event.target.value = '';
                return;
            }

            const formData = new FormData();
            formData.append('file', file);

            this.isUploadingDirectorSignature = true;
            try {
                const data = await window.api.upload('/schools/current/settings/director-signature', formData);
                if (data && data.url) {
                    this.config.directorSignatureUrl = data.url;
                }
            } catch (err) {
                this.directorSignatureUploadError = window.api.toMessage(err, 'Erreur lors de l\'envoi du fichier.');
            } finally {
                this.isUploadingDirectorSignature = false;
                event.target.value = '';
            }
        },

        async uploadSecretarySignatureFile(event) {
            const file = event.target.files && event.target.files[0];
            if (!file) return;

            this.secretarySignatureUploadError = null;
            if (file.size > 2 * 1024 * 1024) {
                this.secretarySignatureUploadError = 'Le fichier dépasse la taille maximale autorisée (2 Mo).';
                event.target.value = '';
                return;
            }

            const allowedTypes = ['image/png', 'image/jpeg', 'image/webp'];
            if (!allowedTypes.includes(file.type)) {
                this.secretarySignatureUploadError = 'Format non supporté. Seuls PNG, JPEG et WEBP sont autorisés.';
                event.target.value = '';
                return;
            }

            const formData = new FormData();
            formData.append('file', file);

            this.isUploadingSecretarySignature = true;
            try {
                const data = await window.api.upload('/schools/current/settings/secretary-signature', formData);
                if (data && data.url) {
                    this.config.secretarySignatureUrl = data.url;
                }
            } catch (err) {
                this.secretarySignatureUploadError = window.api.toMessage(err, 'Erreur lors de l\'envoi du fichier.');
            } finally {
                this.isUploadingSecretarySignature = false;
                event.target.value = '';
            }
        },

        async uploadCashierSignatureFile(event) {
            const file = event.target.files && event.target.files[0];
            if (!file) return;

            this.cashierSignatureUploadError = null;
            if (file.size > 2 * 1024 * 1024) {
                this.cashierSignatureUploadError = 'Le fichier dépasse la taille maximale autorisée (2 Mo).';
                event.target.value = '';
                return;
            }

            const allowedTypes = ['image/png', 'image/jpeg', 'image/webp'];
            if (!allowedTypes.includes(file.type)) {
                this.cashierSignatureUploadError = 'Format non supporté. Seuls PNG, JPEG et WEBP sont autorisés.';
                event.target.value = '';
                return;
            }

            const formData = new FormData();
            formData.append('file', file);

            this.isUploadingCashierSignature = true;
            try {
                const data = await window.api.upload('/schools/current/settings/cashier-signature', formData);
                if (data && data.url) {
                    this.config.cashierSignatureUrl = data.url;
                }
            } catch (err) {
                this.cashierSignatureUploadError = window.api.toMessage(err, 'Erreur lors de l\'envoi du fichier.');
            } finally {
                this.isUploadingCashierSignature = false;
                event.target.value = '';
            }
        },

        async uploadOfficialStampFile(event) {
            const file = event.target.files && event.target.files[0];
            if (!file) return;

            this.officialStampUploadError = null;
            if (file.size > 2 * 1024 * 1024) {
                this.officialStampUploadError = 'Le fichier dépasse la taille maximale autorisée (2 Mo).';
                event.target.value = '';
                return;
            }

            const allowedTypes = ['image/png', 'image/jpeg', 'image/webp'];
            if (!allowedTypes.includes(file.type)) {
                this.officialStampUploadError = 'Format non supporté. Seuls PNG, JPEG et WEBP sont autorisés.';
                event.target.value = '';
                return;
            }

            const formData = new FormData();
            formData.append('file', file);

            this.isUploadingOfficialStamp = true;
            try {
                const data = await window.api.upload('/schools/current/settings/official-stamp', formData);
                if (data && data.url) {
                    this.config.officialStampUrl = data.url;
                }
            } catch (err) {
                this.officialStampUploadError = window.api.toMessage(err, 'Erreur lors de l\'envoi du fichier.');
            } finally {
                this.isUploadingOfficialStamp = false;
                event.target.value = '';
            }
        },

        async uploadSurveillantSignatureFile(event) {
            const file = event.target.files && event.target.files[0];
            if (!file) return;

            this.surveillantSignatureUploadError = null;
            if (file.size > 2 * 1024 * 1024) {
                this.surveillantSignatureUploadError = 'Le fichier dépasse la taille maximale autorisée (2 Mo).';
                event.target.value = '';
                return;
            }

            const allowedTypes = ['image/png', 'image/jpeg', 'image/webp'];
            if (!allowedTypes.includes(file.type)) {
                this.surveillantSignatureUploadError = 'Format non supporté. Seuls PNG, JPEG et WEBP sont autorisés.';
                event.target.value = '';
                return;
            }

            const formData = new FormData();
            formData.append('file', file);

            this.isUploadingSurveillantSignature = true;
            try {
                const data = await window.api.upload('/schools/current/settings/surveillant-signature', formData);
                if (data && data.url) {
                    this.config.surveillantSignatureUrl = data.url;
                }
            } catch (err) {
                this.surveillantSignatureUploadError = window.api.toMessage(err, 'Erreur lors de l\'envoi du fichier.');
            } finally {
                this.isUploadingSurveillantSignature = false;
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
                    allowFinanceToDeleteFees: this.config.allowFinanceToDeleteFees,
                    directorSignatureUrl: this.config.directorSignatureUrl || null,
                    secretarySignatureUrl: this.config.secretarySignatureUrl || null,
                    cashierSignatureUrl: this.config.cashierSignatureUrl || null,
                    officialStampUrl: this.config.officialStampUrl || null,
                    surveillantSignatureUrl: this.config.surveillantSignatureUrl || null,
                    typeEtablissement: this.config.typeEtablissement || 'Prive'
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
                    allowFinanceToDeleteFees: saved.allowFinanceToDeleteFees,
                    directorSignatureUrl: saved.directorSignatureUrl || '',
                    secretarySignatureUrl: saved.secretarySignatureUrl || '',
                    cashierSignatureUrl: saved.cashierSignatureUrl || '',
                    officialStampUrl: saved.officialStampUrl || '',
                    surveillantSignatureUrl: saved.surveillantSignatureUrl || '',
                    typeEtablissement: saved.typeEtablissement || 'Prive'
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

        openCreateMentionFromDefault(mention) {
            this.newMention = { label: mention.label, minAverage: mention.minAverage };
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

        // ------------------------------------------------------------ Édition d'une mention

        openEditMention(mention) {
            if (!mention || !mention.id) return;
            this.editingMention = { id: mention.id, label: mention.label, minAverage: mention.minAverage };
            this.mentionEditErrors = {};
        },

        closeEditMention() {
            this.editingMention = null;
            this.mentionEditErrors = {};
        },

        async submitEditMention() {
            if (!this.editingMention) return;

            this.isSavingMentionEdit = true;
            this.mentionEditErrors = {};
            try {
                await window.api.patch(`/grades/mentions/${this.editingMention.id}`, {
                    label: this.editingMention.label,
                    minAverage: this.editingMention.minAverage
                });
                this.closeEditMention();
                this.mentions = await window.api.get('/grades/mentions');
            } catch (err) {
                this.mentionEditErrors = window.api.toFieldErrors(err, 'Erreur lors de la modification de la mention.');
            } finally {
                this.isSavingMentionEdit = false;
            }
        },

        // ------------------------------------------------------------ Suppression d'une mention

        openDeleteMention(mention) {
            if (!mention || !mention.id) return;
            this.deletingMention = { id: mention.id, label: mention.label };
            this.deleteMentionError = null;
        },

        closeDeleteMention() {
            this.deletingMention = null;
            this.deleteMentionError = null;
        },

        async confirmDeleteMention() {
            if (!this.deletingMention) return;

            this.isDeletingMention = true;
            this.deleteMentionError = null;
            try {
                await window.api.delete(`/grades/mentions/${this.deletingMention.id}`);
                this.deletingMention = null;
                this.mentions = await window.api.get('/grades/mentions');
            } catch (err) {
                this.deleteMentionError = window.api.toMessage(err, 'Erreur lors de la suppression de la mention.');
            } finally {
                this.isDeletingMention = false;
            }
        },

        // ------------------------------------------- Zone de danger (réinitialisation)

        openResetSchool() {
            this.resetConfirmation = '';
            this.resetError = null;
            this.isResetSchoolOpen = true;
        },

        closeResetSchool() {
            // Ne se ferme pas pendant l'appel : la purge est déjà partie côté serveur, laisser croire
            // qu'on l'a annulée en refermant la fenêtre serait mensonger.
            if (this.isResetting) return;

            this.isResetSchoolOpen = false;
            this.resetConfirmation = '';
            this.resetError = null;
        },

        async confirmResetSchool() {
            if (!this.resetConfirmationMatches || this.isResetting) return;

            this.isResetting = true;
            this.resetError = null;
            try {
                const summary = await window.api.post('/schools/current/reset-data', {
                    confirmation: this.resetConfirmation.trim()
                });

                this.isResetSchoolOpen = false;
                this.resetConfirmation = '';
                this.resetSummary = summary;
            } catch (err) {
                this.resetError = window.api.toMessage(err, "La réinitialisation a échoué. Aucune donnée n'a été effacée.");
            } finally {
                this.isResetting = false;
            }
        },

        // Rechargement COMPLET de la page, et pas seulement de cet écran : les compteurs du tableau de
        // bord, les listes d'élèves et les totaux de caisse encore en mémoire dans d'autres composants
        // décriraient une école qui n'existe plus.
        closeResetSummary() {
            this.resetSummary = null;
            window.location.reload();
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
                ? 'bg-white text-indigo-600 font-semibold shadow-sm'
                : 'bg-transparent text-slate-700 font-medium hover:text-slate-900 hover:bg-slate-200/50';
        }
    }));
});
