/**
 * Formulaire PUBLIC d'inscription self-service — ticket JGK-I01.
 *
 * Page entièrement anonyme : aucune session, aucun jeton. On poste vers POST /api/v1/registration-requests
 * et on affiche la référence de suivi retournée. Le mot de passe choisi ne transite QUE dans cette requête
 * HTTPS et n'est jamais restocké côté navigateur.
 *
 * `website` est le champ HONEYPOT : invisible pour un humain, laissé vide. Un bot de remplissage
 * automatique le renseigne et trahit sa nature — le serveur ignore alors silencieusement la soumission.
 */
'use strict';

// Miroir CÔTÉ CLIENT des règles serveur (SubmitRegistrationRequestValidator, PasswordPolicy,
// SenegalPhoneValidation, SafeTextValidation) : un retour immédiat, sans aller-retour réseau. Le
// serveur reste la seule source de vérité — ces mêmes règles y sont réappliquées de toute façon.
// Déclarés hors de l'IIFE ci-dessous car réutilisés par le composant Alpine `registrationForm`
// (voir plus bas) : partager la même portée que window.registration évite un ReferenceError au
// premier clic sur « Envoyer ma demande » (isSafeText, EMAIL_REGEX… hors de portée sinon).
const EMAIL_REGEX = /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
const SENEGAL_PHONE_REGEX = /^(?:\+221|00221)?\s?(?:77|76|78|70|75|33)(?:\s?\d){7}$/;
const UNSAFE_TEXT_REGEX = /[<>]|javascript\s*:|&#/i;
const NAME_REGEX = /^[A-Za-zÀ-ÖØ-öø-ÿ\s-]+$/;
const FORBIDDEN_PASSWORD_SEQUENCES = ['123456', 'azerty', 'qwerty', 'password', 'motdepasse', 'abcdef'];

const isSafeText = (value) => !UNSAFE_TEXT_REGEX.test(value);

/** Prénom/nom : uniquement lettres (accents compris), espaces et tirets, au moins 2 lettres. */
function isValidName(value) {
    if (!NAME_REGEX.test(value)) return false;
    const letterCount = (value.match(/[A-Za-zÀ-ÖØ-öø-ÿ]/g) || []).length;
    return letterCount >= 2;
}

/** Reproduit PasswordPolicy.Validate (C#) : mêmes règles, mêmes messages. */
function passwordPolicyErrors(password, personalTerms) {
    const errors = [];

    if (password.length < 8) errors.push('Le mot de passe doit contenir au moins 8 caractères.');
    if (!/[A-Z]/.test(password)) errors.push('Le mot de passe doit contenir au moins une majuscule.');
    if (!/[a-z]/.test(password)) errors.push('Le mot de passe doit contenir au moins une minuscule.');
    if (!/[0-9]/.test(password)) errors.push('Le mot de passe doit contenir au moins un chiffre.');
    if (password.length > 0 && /^[a-zA-Z0-9]*$/.test(password)) {
        errors.push('Le mot de passe doit contenir au moins un caractère spécial.');
    }

    const lowered = password.toLowerCase();
    if (FORBIDDEN_PASSWORD_SEQUENCES.some((seq) => lowered.includes(seq))) {
        errors.push('Le mot de passe ne doit pas contenir de suite évidente (ex. 123456, password).');
    }

    const personalWords = personalTerms
        .filter(Boolean)
        .flatMap((term) => term.split(' ').map((w) => w.trim()).filter((w) => w.length >= 3));

    if (personalWords.some((word) => lowered.includes(word.toLowerCase()))) {
        errors.push('Le mot de passe ne doit pas contenir votre nom ou celui de l’établissement.');
    }

    return errors;
}

(() => {
    const API_BASE = '/api/v1';

    /**
     * Message affichable quand la réponse n'a pas le format normalisé (ex. échec de model-binding
     * ASP.NET Core avant même le validateur applicatif) — sans traduction, la famille qui s'inscrit en
     * ligne verrait littéralement « Erreur HTTP 400 ».
     */
    function httpFallbackMessage(status) {
        if (status === 400 || status === 422) return "Le formulaire d'inscription contient une information invalide. Vérifiez les champs saisis puis réessayez.";
        if (status >= 500) return 'Le service rencontre une difficulté technique. Réessayez dans quelques instants.';
        return "Votre demande n'a pas pu être envoyée. Réessayez, et contactez l'établissement si le problème persiste.";
    }

    /** Traduit une réponse d'erreur en Error exploitable (corps JSON normalisé, ou vide). */
    async function toError(response) {
        const payload = await response.json().catch(() => null);

        if (payload && payload.message) {
            const error = new Error(payload.message);
            error.code = payload.code;
            error.details = payload.details;
            error.status = response.status;
            return error;
        }

        const fallback = new Error(httpFallbackMessage(response.status));
        fallback.status = response.status;
        return fallback;
    }

    window.registration = {
        async submit(payload) {
            const response = await fetch(`${API_BASE}/registration-requests`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (!response.ok) {
                throw await toError(response);
            }

            return await response.json();
        },

        /** Grille tarifaire par tranche d'effectif (GET public, anonyme) : la seule source des prix affichés. */
        async fetchGrid() {
            const response = await fetch(`${API_BASE}/public/pricing-grid`, { headers: { Accept: 'application/json' } });

            if (!response.ok) {
                throw await toError(response);
            }

            return await response.json();
        }
    };
})();

// Profils proposés à un établissement PRIVÉ. Les identifiants sont ceux de l'API (ProfileType), jamais des libellés.
// `cycles` : le profil est académique (notes, bulletins) — le Directeur doit dire quels cycles il gère. Miroir de
// RegistrationOffer.RequiresCycles côté serveur, qui reste seul juge.
const PRIVATE_PROFILES = [
    { value: 'EnseignementGeneral', label: 'Enseignement général', hint: 'École classique : notes, bulletins, scolarité.', cycles: true },
    { value: 'FrancoArabe', label: 'Franco-arabe', hint: 'Programme bilingue, matières arabes et islamiques.', cycles: true },
    { value: 'DaaraMemorisation', label: 'Daara / école coranique', hint: 'Mémorisation du Coran, suivi par Halqa.', cycles: false },
    { value: 'ComptabiliteRapports', label: 'Comptabilité & gestion financière', hint: 'Caisse, paiements et rapports financiers seuls.', cycles: false }
];

// Cycles cochables. Valeurs de l'API (CycleType), libellés de l'écran du Directeur.
const CYCLE_CHOICES = [
    { value: 'Maternelle', label: 'Préscolaire' },
    { value: 'Primaire', label: 'Élémentaire' },
    { value: 'College', label: 'Moyen' },
    { value: 'Lycee', label: 'Secondaire' }
];

document.addEventListener('alpine:init', () => {
    Alpine.data('registrationForm', () => ({
        // Directeur
        directorFullName: '',
        directorEmail: '',
        directorPhone: '',
        directorPassword: '',
        showPassword: false,

        // Établissement
        schoolName: '',
        schoolAddress: '',
        city: '',
        region: '',
        estimatedStudentCount: '',

        // Type d'établissement : AUCUNE valeur par défaut — le choix est explicite, sans quoi un établissement public se
        // retrouverait « privé » par simple inattention. ownership : 'Private' | 'Public'.
        ownership: '',

        // PUBLIC (grille historique, facturé par élève sur devis) : un seul cycle, Primaire | College | Lycee.
        cycleProfile: '',

        // PRIVÉ — offre par tranche d'effectif : profil, internat, cycles cochés, tranche. Le prix affiché vient de la
        // grille publique du serveur ; il n'est qu'une indication, le serveur recalcule tout à l'inscription.
        profile: '',
        hasBoarding: false,
        cycles: [],
        tier: '',
        // Vrai quand le Directeur a lui-même cliqué une tranche : on ne la lui reprend plus (on la RELÈVE seulement si
        // l'effectif saisi l'exige). Tant qu'il n'a rien choisi, la tranche suit l'effectif.
        tierChosenByUser: false,
        grid: [],
        quoteAboveStudents: 400,
        gridError: null,
        pendingTier: null,

        // Honeypot : DOIT rester vide. Lié au champ leurre `website`.
        website: '',

        isSubmitting: false,
        error: null,
        errors: {},
        trackingReference: null,

        // Pré-remplit le profil quand on arrive depuis une carte tarifaire de la vitrine
        // (/inscription?type=prive&tranche=Tier_100 ou ?type=public&cycles=College) — une simple commodité d'affichage,
        // jamais fait confiance côté serveur : SubmitRegistrationRequestValidator revalide toute l'offre.
        init() {
            const params = new URLSearchParams(window.location.search);
            const type = { prive: 'Private', public: 'Public' }[params.get('type')];

            // La grille se charge dans tous les cas : un privé choisi à la main en a besoin tout de suite.
            this.pendingTier = params.get('tranche');
            this.loadGrid();

            if (!type) return;

            this.selectOwnership(type);

            if (type === 'Public') {
                const cycles = params.get('cycles');
                if (this.cycleOptions.some((o) => o.value === cycles)) this.cycleProfile = cycles;
            } else {
                const profil = params.get('profil');
                if (PRIVATE_PROFILES.some((p) => p.value === profil)) this.selectProfile(profil);
            }
        },

        async loadGrid() {
            try {
                const grid = await window.registration.fetchGrid();
                this.grid = grid.tiers || [];
                this.quoteAboveStudents = grid.quoteAboveStudents || 400;
                this.gridError = null;

                if (this.pendingTier && this.grid.some((row) => row.tier === this.pendingTier)) {
                    this.selectTier(this.pendingTier);
                }
                this.pendingTier = null;
                this.syncTier();
            } catch (err) {
                this.gridError = "Les tarifs n'ont pas pu être chargés. Rechargez la page pour choisir votre tranche.";
            }
        },

        // ------------------------------------------------------------------------------------------ Type et cycles

        get isPrivate() {
            return this.ownership === 'Private';
        },

        /** Un public gère UN cycle ; un privé choisit un profil puis, s'il est académique, ses cycles. */
        get cycleOptions() {
            if (this.ownership !== 'Public') return [];
            return [
                { value: 'Primaire', label: 'École élémentaire (primaire)' },
                { value: 'College', label: "Collège d'enseignement moyen (CEM)" },
                { value: 'Lycee', label: 'Lycée' }
            ];
        },

        get profileOptions() {
            return PRIVATE_PROFILES;
        },

        get cycleChoices() {
            return CYCLE_CHOICES;
        },

        /** Les cycles ne se demandent que pour un profil académique : pas pour un Daara pur ni une Comptabilité. */
        get needsCycles() {
            return this.isPrivate && PRIVATE_PROFILES.some((p) => p.value === this.profile && p.cycles);
        },

        /** Changer public/privé efface ce qui n'existe pas dans l'autre offre. */
        selectOwnership(value) {
            this.ownership = value;

            if (value === 'Public') {
                this.profile = '';
                this.hasBoarding = false;
                this.cycles = [];
                this.tier = '';
                this.tierChosenByUser = false;
            } else {
                this.cycleProfile = '';
                this.syncTier();
            }

            this.errors = {
                ...this.errors, ownership: undefined, cycleprofile: undefined, profile: undefined,
                cycles: undefined, tier: undefined, estimatedstudentcount: undefined
            };
        },

        selectProfile(value) {
            this.profile = value;
            if (!this.needsCycles) this.cycles = [];
            this.errors = { ...this.errors, profile: undefined, cycles: undefined };
        },

        isCycleChecked(value) {
            return this.cycles.includes(value);
        },

        toggleCycle(value) {
            this.cycles = this.isCycleChecked(value)
                ? this.cycles.filter((c) => c !== value)
                : CYCLE_CHOICES.map((c) => c.value).filter((c) => c === value || this.cycles.includes(c));
            this.errors = { ...this.errors, cycles: undefined };
        },

        // ------------------------------------------------------------------------------------------ Effectif et tranche

        /** Effectif saisi si c'est un entier strictement positif, sinon null. */
        get declaredCount() {
            if (this.estimatedStudentCount === '' || this.estimatedStudentCount === null) return null;
            const count = Number(this.estimatedStudentCount);
            return Number.isInteger(count) && count > 0 ? count : null;
        },

        /** Au-delà de la dernière tranche standard : « Grand Groupe », sur devis, pas de libre-service. */
        get requiresQuote() {
            return this.isPrivate && this.declaredCount !== null && this.declaredCount > this.quoteAboveStudents;
        },

        /** Plus petite tranche qui contient l'effectif saisi ; null sans effectif, sans grille ou sur devis. */
        get minimumTier() {
            if (this.declaredCount === null || this.requiresQuote) return null;
            const row = this.grid.find((t) => t.maxStudents >= this.declaredCount);
            return row ? row.tier : null;
        },

        tierIndex(tier) {
            return this.grid.findIndex((row) => row.tier === tier);
        },

        /** Une tranche plus petite que celle de l'effectif saisi n'est pas choisissable (anti-fraude, aussi côté serveur). */
        isTierDisabled(tier) {
            const minimum = this.minimumTier;
            return minimum !== null && this.tierIndex(tier) < this.tierIndex(minimum);
        },

        /** Prix d'une tranche, majoration Internat comprise si l'école héberge des élèves. */
        priceOf(row) {
            return {
                monthly: row.monthlyXof + (this.hasBoarding ? row.boardingSurchargeMonthlyXof : 0),
                annual: row.annualXof + (this.hasBoarding ? row.boardingSurchargeAnnualXof : 0)
            };
        },

        get tierOptions() {
            return this.grid.map((row, index) => {
                const previous = index > 0 ? this.grid[index - 1].maxStudents : null;
                const price = this.priceOf(row);
                return {
                    value: row.tier,
                    label: previous === null ? `Jusqu'à ${row.maxStudents} élèves` : `${previous + 1} à ${row.maxStudents} élèves`,
                    monthly: price.monthly,
                    annual: price.annual,
                    disabled: this.isTierDisabled(row.tier)
                };
            });
        },

        /** Tranche choisie avec son prix, pour le récapitulatif ; null tant qu'aucune n'est choisie. */
        get quote() {
            const row = this.grid.find((t) => t.tier === this.tier);
            if (!row) return null;
            const price = this.priceOf(row);
            const months = Math.round((price.annual / price.monthly) * 10) / 10;
            return { monthly: price.monthly, annual: price.annual, paidMonths: months, boarding: this.hasBoarding };
        },

        selectTier(value) {
            if (this.isTierDisabled(value)) return;
            this.tier = value;
            this.tierChosenByUser = true;
            this.errors = { ...this.errors, tier: undefined };
        },

        /**
         * Appelé à chaque saisie d'effectif : présélectionne la tranche qui le contient tant que le Directeur n'a pas
         * choisi lui-même, et ne le laisse jamais sous la tranche minimale. Sur devis, aucune tranche n'est retenue.
         */
        syncTier() {
            if (!this.isPrivate) return;

            const minimum = this.minimumTier;

            if (this.requiresQuote) {
                this.tier = '';
                this.tierChosenByUser = false;
                return;
            }

            if (minimum === null) return;

            if (!this.tierChosenByUser || this.tier === '' || this.isTierDisabled(this.tier)) {
                this.tier = minimum;
                this.tierChosenByUser = false;
            }
        },

        formatXof(amount) {
            return new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'XOF', maximumFractionDigits: 0 }).format(amount);
        },

        // ------------------------------------------------------------------------------------------ Validation

        /**
         * Validation CLIENT avant tout envoi réseau : champs requis, formats (e-mail, téléphone),
         * robustesse du mot de passe, caractères interdits (XSS). Clés en MINUSCULES pour rester
         * compatibles avec window.api.toFieldErrors, qui aplatit de la même façon les erreurs 422
         * renvoyées par le serveur — un même x-show="errors.xxx" couvre les deux origines.
         */
        validate() {
            const errors = {};

            if (!this.directorFullName) {
                errors.directorfullname = 'Le nom complet est obligatoire.';
            } else if (!isValidName(this.directorFullName)) {
                errors.directorfullname =
                    'Le nom complet doit contenir au moins 2 lettres, uniquement des lettres, espaces et tirets.';
            }

            if (!this.directorEmail) {
                errors.directoremail = "L'adresse e-mail est obligatoire.";
            } else if (!EMAIL_REGEX.test(this.directorEmail)) {
                errors.directoremail = "Le format de l'adresse e-mail est invalide.";
            }

            if (!this.directorPhone) {
                errors.directorphone = 'Le numéro de téléphone est obligatoire.';
            } else if (!SENEGAL_PHONE_REGEX.test(this.directorPhone)) {
                errors.directorphone = 'Le numéro doit être un numéro sénégalais valide (ex: 77 123 45 67).';
            }

            if (!this.directorPassword) {
                errors.directorpassword = 'Le mot de passe est obligatoire.';
            } else {
                const passwordErrors = passwordPolicyErrors(
                    this.directorPassword, [this.directorFullName, this.schoolName]);
                if (passwordErrors.length) errors.directorpassword = passwordErrors[0];
            }

            if (!this.schoolName) {
                errors.schoolname = "Le nom de l'établissement est obligatoire.";
            } else if (!isSafeText(this.schoolName)) {
                errors.schoolname = "Le nom de l'établissement contient des caractères interdits.";
            }

            if (this.schoolAddress && !isSafeText(this.schoolAddress)) {
                errors.schooladdress = "L'adresse contient des caractères interdits.";
            }
            if (this.city && !isSafeText(this.city)) {
                errors.city = 'La ville contient des caractères interdits.';
            }
            if (this.region && !isSafeText(this.region)) {
                errors.region = 'La région contient des caractères interdits.';
            }

            if (this.estimatedStudentCount !== '' && this.estimatedStudentCount !== null) {
                const count = Number(this.estimatedStudentCount);
                if (!Number.isInteger(count) || count <= 0 || count > 100000) {
                    errors.estimatedstudentcount = "L'effectif doit être un nombre entier entre 1 et 100 000.";
                }
            }

            if (!this.ownership) {
                errors.ownership = 'Précisez si votre établissement est public ou privé.';
            }

            if (this.ownership === 'Public' && !this.cycleProfile) {
                errors.cycleprofile = 'Précisez le cycle géré par votre établissement.';
            }

            if (this.isPrivate) {
                this.validatePrivateOffer(errors);
            }

            this.errors = errors;
            return Object.keys(errors).length === 0;
        },

        /** Offre par tranche : mêmes règles que SubmitRegistrationRequestValidator, pour un retour sans aller-retour. */
        validatePrivateOffer(errors) {
            if (!this.profile) {
                errors.profile = "Précisez le profil d'enseignement de votre établissement.";
            }

            if (this.needsCycles && this.cycles.length === 0) {
                errors.cycles = 'Cochez au moins un cycle géré par votre établissement.';
            }

            if (this.estimatedStudentCount === '' || this.estimatedStudentCount === null) {
                errors.estimatedstudentcount = "Indiquez l'effectif exact de votre établissement.";
            }

            if (this.requiresQuote) {
                errors.tier = `Au-delà de ${this.quoteAboveStudents} élèves, l'offre « Grand Groupe » est établie sur devis : ` +
                    'contactez-nous pour finaliser votre inscription.';
            } else if (this.gridError) {
                errors.tier = this.gridError;
            } else if (!this.tier) {
                errors.tier = "Choisissez la tranche d'effectif de votre établissement.";
            } else if (this.isTierDisabled(this.tier)) {
                errors.tier = "Cette tranche est trop petite pour l'effectif indiqué : choisissez une tranche supérieure.";
            }
        },

        /**
         * Validation DYNAMIQUE au départ du champ (x-on:blur) : nettoie les espaces superflus puis
         * revalide tout le formulaire, pour un retour immédiat sans attendre la soumission.
         */
        touch(field) {
            // Jamais le mot de passe : un espace y est un caractère valide, le rogner changerait le
            // secret saisi par l'utilisateur (même raisonnement que dans submit()).
            if (field !== 'directorPassword' && typeof this[field] === 'string') {
                this[field] = this[field].trim();
            }
            this.validate();
        },

        /** Corps de la demande : l'offre par tranche pour un privé, l'offre historique (un cycle) pour un public. */
        buildPayload() {
            const base = {
                directorFullName: this.directorFullName,
                directorEmail: this.directorEmail,
                directorPhone: this.directorPhone,
                directorPassword: this.directorPassword,
                schoolName: this.schoolName,
                schoolAddress: this.schoolAddress || null,
                city: this.city || null,
                region: this.region || null,
                // Facultatif pour un public (chaîne vide -> null), obligatoire pour un privé (validé plus haut).
                estimatedStudentCount: this.estimatedStudentCount === '' ? null : Number(this.estimatedStudentCount),
                ownership: this.ownership,
                website: this.website
            };

            if (this.isPrivate) {
                // Aucun montant, aucun plafond : seul le choix. Le plan d'abonnement et le prix, le serveur les déduit.
                return {
                    ...base,
                    profile: this.profile,
                    hasBoarding: this.hasBoarding,
                    cycles: this.needsCycles ? [...this.cycles] : null,
                    tier: this.tier
                };
            }

            return { ...base, cycleProfile: this.cycleProfile, sizeTier: null };
        },

        async submit() {
            this.error = null;
            this.errors = {};

            // Nettoyage des espaces superflus AVANT validation et envoi (jamais le mot de passe : un
            // espace y est un caractère valide, le rogner changerait le secret saisi par l'utilisateur).
            this.directorFullName = this.directorFullName.trim();
            this.directorEmail = this.directorEmail.trim();
            this.directorPhone = this.directorPhone.trim();
            this.schoolName = this.schoolName.trim();
            this.schoolAddress = this.schoolAddress.trim();
            this.city = this.city.trim();
            this.region = this.region.trim();

            if (!this.validate()) {
                return;
            }

            this.isSubmitting = true;

            try {
                const result = await window.registration.submit(this.buildPayload());

                // Succès : on n'affiche plus le formulaire, seulement la référence à conserver. Le mot de
                // passe saisi est abandonné avec le composant — jamais restocké.
                this.trackingReference = result.trackingReference;
            } catch (err) {
                if (err.status === 429) {
                    this.error = 'Trop de demandes envoyées depuis votre connexion. Réessayez dans quelques minutes.';
                } else {
                    // Validation (422) : `details` est un DICTIONNAIRE { "Champ": ["motif"] }
                    // (ExceptionHandlingMiddleware y place le dictionnaire de FluentValidation).
                    // toFieldErrors l'aplatit en { champ_en_minuscule: premier_motif }, mêmes clés que
                    // validate() ci-dessus — un seul jeu de x-show="errors.xxx" couvre les deux cas.
                    this.errors = window.api.toFieldErrors(err, 'Envoi impossible. Vérifiez votre réseau et réessayez.');
                    this.error = this.errors.global || null;
                }
            } finally {
                this.isSubmitting = false;
            }
        }
    }));
});
