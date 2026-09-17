/**
 * Écran Paramètres — regroupe TOUT ce que le Directeur configure pour son établissement.
 *
 * NAVIGATION : barre d'onglets HORIZONTALE (pill tabs, composant .tab-nav-scroll) sous le titre.
 * Elle remplace le sidebar vertical Hub & Spoke du ticket JGK-UI02, lui-même issu des 7 onglets
 * horizontaux d'origine — à chaque fois, même état, mêmes appels API, seule la PRÉSENTATION change.
 * `tab` reste la seule source de vérité de "quel panneau est affiché" ; la barre d'onglets (dans
 * Views/Settings/Index.cshtml) ne fait qu'écrire dedans via goToTab(). Deux appels API historiques
 * portent toujours l'essentiel des champs, RÉPARTIS entre plusieurs sections :
 *   1. /schools/current (saveProfile) : identité, mentions légales, en-tête académique du
 *      bulletin, intégration étatique (SIMEN/GPS) — spokes "profil" et "integration-etatique",
 *      pilier Identité & Conformité.
 *   2. /schools/current/settings (saveConfig) : format de date, déconnexion auto, mensualités/an,
 *      formats de matricule, signatures/cachet, type d'établissement, délégations — désormais
 *      réparti entre les spokes "formats-signatures" (Identité), "pedagogie" (délégation
 *      notation), "finance" (mensualités, délégations de caisse) et "securite" (date, déconnexion,
 *      type d'établissement). Un SEUL objet `config` réactif : peu importe quel spoke est visible
 *      au moment du clic sur "Enregistrer", c'est TOUJOURS l'état complet qui part au serveur —
 *      voir saveConfig() plus bas, inchangée.
 *   Les mentions du bulletin (spoke "pedagogie") restent sur /grades/mentions, ressource à part.
 *   Années scolaires : géré par schoolYearsView() (js/school-years.js), monté dans son spoke.
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
        // Onglet actif. Depuis la refonte UI/UX : barre d'onglets HORIZONTALE (pill tabs) sous le
        // titre, à la place du sidebar vertical Hub & Spoke (ticket JGK-UI02). L'état ne change
        // pas — `tab` reste la seule source de vérité de "quel panneau est affiché", goToTab()
        // écrit dedans + synchronise l'URL. 'profil' est le défaut ; validTabs (init()) fait le
        // pont avec les anciens identifiants ('etablissement', 'configuration') d'un lien externe.
        tab: 'profil',

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
            gpsLatitude: '', gpsLongitude: '', gpsCoordinates: '',
            // Annuaire public (B2C) — consentement de publication, FAUX par défaut (voir
            // School.IsPubliclyListed). Ville/région/présentation ne servent qu'à cette vitrine.
            isPubliclyListed: false, city: '', region: '', publicDescription: ''
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
            // Modules activés/désactivés par le Directeur (Paramètres › Modules & fonctionnalités) —
            // second axe, indépendant de la formule d'abonnement (featureGate/window.features).
            isPedagogyEnabled: true,
            isFinanceEnabled: true,
            isInternatEnabled: false,
            isCoranModuleEnabled: false,
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

        // --- Compteur de départ des matricules (Option 1) — année scolaire en cours ---
        // matriculeSequences vient de GET /schools/current/settings/matricule-sequences ; input
        // porte la valeur éditable (« prochain numéro »), initialisée sur nextValue au chargement.
        matriculeSequences: { student: null, teacher: null },
        matriculeSeqInput: { student: null, teacher: null },
        matriculeSeqSaving: { student: false, teacher: false },
        matriculeSeqError: { student: null, teacher: null },
        matriculeSeqSaved: { student: false, teacher: false },
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
        // Getter, PAS une valeur figée au chargement : dépend de config.isPedagogyEnabled (module
        // Pédagogie, Paramètres › Modules), connu seulement après load() — les mentions du bulletin
        // n'ont pas leur place sur une école qui a désactivé la Pédagogie (GradesController, qui sert
        // /grades/mentions, est sous [RequireModule(SchoolModule.Pedagogy)] côté serveur). Avant le
        // premier chargement, config.isPedagogyEnabled vaut true (défaut sûr) : la visibilité par rôle
        // seule s'applique le temps que load() confirme l'état réel du module.
        get canViewMentions() {
            return this.config.isPedagogyEnabled
                && (window.auth.role === 'Directeur' || window.auth.role === 'Secretariat' || window.auth.role === 'Enseignant');
        },
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

        // --- Bac à sable / mode réel ---
        // isLiveMode pilote les DEUX régimes de la Zone de danger : test (purge + « Passer en mode
        // réel ») ou réel (lecture seule + éventuellement « Repasser en mode test »). Chargé dans
        // load() depuis GET /schools/current/mode ; le serveur revérifie tout (GoLiveCommandHandler,
        // ResetSchoolDataCommandHandler), cet état n'est qu'un confort d'affichage.
        isLiveMode: false,
        wentLiveAt: null,
        // Drapeau d'ENVIRONNEMENT (Development ou SAMA_RETOUR_MODE_TEST_AUTORISE=true), pas un
        // process.env côté front : c'est l'API qui décide si le bouton « Repasser en mode test »
        // existe. Toujours faux en vraie production.
        revertToTestAvailable: false,
        isGoLiveOpen: false,
        goLiveConfirmation: '',
        goLiveError: null,
        isGoingLive: false,
        isRevertingToTest: false,
        revertToTestError: null,

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

        // Même garde que resetConfirmationMatches, mot-clé « CONFIRMER » — miroir exact de
        // GoLiveConfirmation.Matches côté serveur (mot-clé à la casse, nom d'école tolérant).
        get goLiveConfirmationMatches() {
            const typed = (this.goLiveConfirmation || '').trim();
            if (!typed) return false;

            const schoolName = (this.profile.name || '').trim();
            return typed === 'CONFIRMER'
                || (schoolName !== '' && typed.toLocaleLowerCase() === schoolName.toLocaleLowerCase());
        },

        // Le compte rendu du serveur liste TOUTES les tables, y compris celles à 0 ligne (utile au
        // diagnostic) ; l'écran, lui, n'affiche que ce qui a réellement bougé.
        get resetEntriesWithRows() {
            if (!this.resetSummary || !this.resetSummary.entries) return [];
            return this.resetSummary.entries.filter(entry => entry.rowsDeleted > 0);
        },

        init() {
            // Table des identifiants d'onglet valides (JGK-UI02) : chaque spoke du nouveau sidebar,
            // PLUS les deux anciens identifiants ('etablissement', 'configuration') encore portés
            // par un lien externe éventuel ou une habitude d'utilisateur — ils redirigent vers le
            // premier spoke du pilier qui a hérité de leur contenu, jamais une page morte.
            const legacyRedirect = { etablissement: 'profil', configuration: 'pedagogie' };
            const validTabs = [
                'profil', 'integration-etatique', 'formats-signatures', 'annees-scolaires',
                'pedagogie',
                'finance', 'facturation',
                'securite', 'utilisateurs', 'journal-audit', 'sms'
            ];

            const requested = new URLSearchParams(window.location.search).get('tab');
            if (validTabs.includes(requested)) {
                this.tab = requested;
            } else if (requested in legacyRedirect) {
                this.tab = legacyRedirect[requested];
            }

            this.load();
        },

        async load() {
            this.isLoading = true;
            this.loadError = null;
            try {
                const [profile, config, mode] = await Promise.all([
                    window.api.get('/schools/current'),
                    window.api.get('/schools/current/settings'),
                    window.api.get('/schools/current/mode')
                ]);

                this.isLiveMode = !!(mode && mode.isLive);
                this.wentLiveAt = mode ? mode.wentLiveAt : null;
                this.revertToTestAvailable = !!(mode && mode.revertToTestAvailable);

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
                    isPedagogyEnabled: config.isPedagogyEnabled,
                    isFinanceEnabled: config.isFinanceEnabled,
                    isInternatEnabled: config.isInternatEnabled,
                    isCoranModuleEnabled: config.isCoranModuleEnabled,
                    directorSignatureUrl: config.directorSignatureUrl || '',
                    secretarySignatureUrl: config.secretarySignatureUrl || '',
                    cashierSignatureUrl: config.cashierSignatureUrl || '',
                    officialStampUrl: config.officialStampUrl || '',
                    surveillantSignatureUrl: config.surveillantSignatureUrl || '',
                    typeEtablissement: config.typeEtablissement || 'Prive'
                };

                // Compteurs de matricules : requête à part (Directeur seul), pour ne pas fragiliser
                // le Promise.all ci-dessus avec un troisième appel conditionnel.
                if (this.isDirecteur) {
                    this.loadMatriculeSequences();
                }

                // Mentions du bulletin : même raisonnement, ET pour une raison de plus depuis le module
                // Pédagogie — canViewMentions ne peut être évalué correctement qu'UNE FOIS config chargé
                // (il dépend de config.isPedagogyEnabled), donc après le Promise.all ci-dessus, jamais
                // avant. Un échec ici (école qui a désactivé la Pédagogie entre-temps, MODULE_DISABLED)
                // ne doit sous aucun prétexte faire échouer l'affichage du reste de l'écran.
                if (this.canViewMentions) {
                    this.loadMentions();
                }
            } catch (err) {
                this.loadError = window.api.toMessage(err, 'Erreur lors du chargement des paramètres.');
            } finally {
                this.isLoading = false;
            }
        },

        /** Charge les mentions du bulletin — séparé de load() (voir son commentaire d'appel). */
        async loadMentions() {
            try {
                this.mentions = await window.api.get('/grades/mentions');
            } catch {
                // Non bloquant : le reste de l'écran reste utilisable, l'onglet se referme de lui-même
                // au prochain rendu si canViewMentions est entre-temps repassé à faux.
                this.mentions = [];
            }
        },

        /** Charge l'état des compteurs de matricules de l'année en cours et amorce les champs éditables. */
        async loadMatriculeSequences() {
            try {
                const dto = await window.api.get('/schools/current/settings/matricule-sequences');
                this.matriculeSequences = { student: dto.student, teacher: dto.teacher };
                this.matriculeSeqInput = {
                    student: dto.student ? dto.student.nextValue : null,
                    teacher: dto.teacher ? dto.teacher.nextValue : null
                };
            } catch (err) {
                // Non bloquant : le reste de l'écran reste utilisable.
                this.matriculeSeqError = {
                    student: window.api.toMessage(err, 'Compteurs de matricules indisponibles.'),
                    teacher: null
                };
            }
        },

        /**
         * Fixe le prochain numéro de matricule pour l'année en cours. `kind` vaut 'student' ou
         * 'teacher' côté écran ; l'API attend 'Student' / 'Teacher'.
         */
        async saveMatriculeSequence(kind) {
            const nextValue = Number(this.matriculeSeqInput[kind]);
            if (!Number.isInteger(nextValue) || nextValue < 1) {
                this.matriculeSeqError = { ...this.matriculeSeqError, [kind]: 'Saisissez un entier supérieur ou égal à 1.' };
                return;
            }

            this.matriculeSeqSaving = { ...this.matriculeSeqSaving, [kind]: true };
            this.matriculeSeqError = { ...this.matriculeSeqError, [kind]: null };
            this.matriculeSeqSaved = { ...this.matriculeSeqSaved, [kind]: false };
            try {
                const apiKind = kind === 'student' ? 'Student' : 'Teacher';
                const info = await window.api.put('/schools/current/settings/matricule-sequences', {
                    kind: apiKind,
                    nextValue
                });
                this.matriculeSequences = { ...this.matriculeSequences, [kind]: info };
                this.matriculeSeqInput = { ...this.matriculeSeqInput, [kind]: info.nextValue };
                this.matriculeSeqSaved = { ...this.matriculeSeqSaved, [kind]: true };
                setTimeout(() => {
                    this.matriculeSeqSaved = { ...this.matriculeSeqSaved, [kind]: false };
                }, 2500);
            } catch (err) {
                this.matriculeSeqError = {
                    ...this.matriculeSeqError,
                    [kind]: window.api.toMessage(err, "Le prochain numéro n'a pas pu être enregistré.")
                };
            } finally {
                this.matriculeSeqSaving = { ...this.matriculeSeqSaving, [kind]: false };
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
                gpsCoordinates: dto.gpsCoordinates || '',
                isPubliclyListed: !!dto.isPubliclyListed,
                city: dto.city || '',
                region: dto.region || '',
                publicDescription: dto.publicDescription || ''
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
                    gpsLongitude: Number.isFinite(lon) ? lon : null,
                    isPubliclyListed: this.profile.isPubliclyListed,
                    city: this.profile.city || null,
                    region: this.profile.region || null,
                    publicDescription: this.profile.publicDescription || null
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
                    isPedagogyEnabled: this.config.isPedagogyEnabled,
                    isFinanceEnabled: this.config.isFinanceEnabled,
                    isInternatEnabled: this.config.isInternatEnabled,
                    isCoranModuleEnabled: this.config.isCoranModuleEnabled,
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
                    isPedagogyEnabled: saved.isPedagogyEnabled,
                    isFinanceEnabled: saved.isFinanceEnabled,
                    isInternatEnabled: saved.isInternatEnabled,
                    isCoranModuleEnabled: saved.isCoranModuleEnabled,
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

        // ------------------------------------------- Bac à sable → mode réel

        openGoLive() {
            this.goLiveConfirmation = '';
            this.goLiveError = null;
            this.isGoLiveOpen = true;
        },

        closeGoLive() {
            // Ne se ferme pas pendant l'appel : le rechargement de page qui suit un succès s'en charge.
            if (this.isGoingLive) return;
            this.isGoLiveOpen = false;
            this.goLiveConfirmation = '';
            this.goLiveError = null;
        },

        async confirmGoLive() {
            if (!this.goLiveConfirmationMatches || this.isGoingLive) return;

            this.isGoingLive = true;
            this.goLiveError = null;
            try {
                await window.api.post('/schools/current/go-live', {
                    confirmation: this.goLiveConfirmation.trim()
                });

                // Rechargement COMPLET : la pastille « Mode test » de la barre supérieure, les deux
                // régimes de la Zone de danger et l'assistant de démarrage doivent tous refléter le
                // nouveau régime. isGoingLive reste vrai — la page part.
                window.location.reload();
            } catch (err) {
                this.goLiveError = window.api.toMessage(err, "Le passage en mode réel a échoué. Aucun changement n'a été enregistré.");
                this.isGoingLive = false;
            }
        },

        // ---------------------------- Retour mode test (recette / environnements jetables uniquement)

        async revertToTest() {
            // Le bouton reste AFFICHÉ en production, mais désactivé (voir Settings/Index.cshtml) :
            // cette garde évite l'appel — et le 404 du routeur — si un clic passait quand même.
            if (!this.revertToTestAvailable || this.isRevertingToTest) return;

            this.isRevertingToTest = true;
            this.revertToTestError = null;
            try {
                // Endpoint monté seulement si revertToTestAvailable ; un 404 ici signifie « pas sur cet
                // environnement » — le message générique convient.
                await window.api.post('/schools/current/dev/revert-to-test');
                window.location.reload();
            } catch (err) {
                this.revertToTestError = window.api.toMessage(err, "Le retour en mode test a échoué.");
                this.isRevertingToTest = false;
            }
        },

        // Même contournement du sérialiseur decimal que Subjects.formatCoefficient (16.00 → 16).
        formatAverage(value) {
            return Number(value).toLocaleString('fr-FR');
        },

        // ---------------------------------------------------------------- Affichage (barre d'onglets)

        /**
         * Bascule vers la section demandée et synchronise l'URL via replaceState — un lien
         * copié/rechargé rouvre le même onglet, sans naviguer ni recharger les données (même esprit
         * que les "sous-routes" du ticket, sans le coût d'un vrai changement de page : l'état déjà
         * chargé — profil, config, mentions… — reste intact).
         */
        goToTab(name) {
            this.tab = name;

            const url = new URL(window.location.href);
            url.searchParams.set('tab', name);
            window.history.replaceState({}, '', url);
        },

        /** État visuel d'un onglet de la barre horizontale. Renvoie le SEUL modificateur
         * `tab-btn-active` (pastille bleu Unikol plein, texte blanc — défini dans input.css) ;
         * la base `.tab-btn` est posée en dur dans la vue. Même composant partagé que Paie,
         * Inventaire, Examens, Frais… pour que toutes les barres d'onglets se lisent à l'identique. */
        tabClass(name) {
            return this.tab === name ? 'tab-btn-active' : '';
        }
    }));
});
