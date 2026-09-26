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
        }
    };
})();

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

        // Profil tarifaire (grille de la vitrine) : AUCUNE valeur par défaut — le choix est explicite, sans quoi un
        // établissement public se retrouverait « privé » par simple inattention. ownership : 'Private' | 'Public' ;
        // cycleProfile : Primaire | College | Lycee | Bicycle | Complexe ; sizeTier : Small | Medium | Large.
        ownership: '',
        cycleProfile: '',
        sizeTier: '',

        // Honeypot : DOIT rester vide. Lié au champ leurre `website`.
        website: '',

        isSubmitting: false,
        error: null,
        errors: {},
        trackingReference: null,

        // Pré-remplit le profil quand on arrive depuis une carte tarifaire de la vitrine
        // (/inscription?type=prive&cycles=Bicycle&taille=Medium) — une simple commodité d'affichage, jamais fait
        // confiance côté serveur : SubmitRegistrationRequestValidator revalide toute la combinaison.
        init() {
            const params = new URLSearchParams(window.location.search);
            const type = { prive: 'Private', public: 'Public' }[params.get('type')];
            if (!type) return;

            this.selectOwnership(type);

            const cycles = params.get('cycles');
            if (this.cycleOptions.some((o) => o.value === cycles)) this.cycleProfile = cycles;

            const taille = params.get('taille');
            if (this.needsSize && ['Small', 'Medium', 'Large'].includes(taille)) this.sizeTier = taille;
        },

        /** Cycles proposés : un public gère UN cycle ; un privé peut en réunir deux, ou tous (grille de la vitrine). */
        get cycleOptions() {
            if (this.ownership === 'Public') {
                return [
                    { value: 'Primaire', label: 'École élémentaire (primaire)' },
                    { value: 'College', label: "Collège d'enseignement moyen (CEM)" },
                    { value: 'Lycee', label: 'Lycée' }
                ];
            }
            return [
                { value: 'Primaire', label: 'Primaire uniquement' },
                { value: 'College', label: 'Collège uniquement' },
                { value: 'Lycee', label: 'Lycée uniquement' },
                { value: 'Bicycle', label: 'Deux cycles (ex. Primaire + Collège)' },
                { value: 'Complexe', label: 'Maternelle, Primaire, Collège et Lycée' }
            ];
        },

        /** Le forfait privé dépend de la taille ; le public est facturé par élève, sans palier. */
        get needsSize() {
            return this.ownership === 'Private';
        },

        /** Paliers de taille, avec les seuils d'effectif de la grille pour les bicycles et grands complexes. */
        get sizeOptions() {
            if (this.cycleProfile === 'Bicycle') {
                return [
                    { value: 'Small', label: 'Moins de 400 élèves' },
                    { value: 'Medium', label: '400 à 800 élèves' },
                    { value: 'Large', label: 'Plus de 800 élèves' }
                ];
            }
            if (this.cycleProfile === 'Complexe') {
                return [
                    { value: 'Small', label: 'Moins de 500 élèves' },
                    { value: 'Medium', label: '500 à 1 000 élèves' },
                    { value: 'Large', label: 'Plus de 1 000 élèves' }
                ];
            }
            return [
                { value: 'Small', label: 'Petit établissement' },
                { value: 'Medium', label: 'Établissement moyen' },
                { value: 'Large', label: 'Grand établissement' }
            ];
        },

        /** Changer public/privé invalide ce qui n'existe plus dans l'autre grille (cycle bicycle, palier de taille). */
        selectOwnership(value) {
            this.ownership = value;
            if (!this.cycleOptions.some((o) => o.value === this.cycleProfile)) this.cycleProfile = '';
            if (!this.needsSize) this.sizeTier = '';
            this.errors = { ...this.errors, ownership: undefined, cycleprofile: undefined, sizetier: undefined };
        },

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
                    errors.estimatedstudentcount = "L'effectif estimé doit être un nombre entier entre 1 et 100 000.";
                }
            }

            // Profil tarifaire : choix explicite, cohérent avec la grille (un public n'a pas de palier de taille).
            if (!this.ownership) {
                errors.ownership = 'Précisez si votre établissement est public ou privé.';
            }
            if (this.ownership && !this.cycleProfile) {
                errors.cycleprofile = 'Précisez les cycles gérés par votre établissement.';
            }
            if (this.needsSize && !this.sizeTier) {
                errors.sizetier = 'Précisez la taille de votre établissement.';
            }

            this.errors = errors;
            return Object.keys(errors).length === 0;
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
                const result = await window.registration.submit({
                    directorFullName: this.directorFullName,
                    directorEmail: this.directorEmail,
                    directorPhone: this.directorPhone,
                    directorPassword: this.directorPassword,
                    schoolName: this.schoolName,
                    schoolAddress: this.schoolAddress || null,
                    city: this.city || null,
                    region: this.region || null,
                    // Champ facultatif : chaîne vide -> null, sinon entier.
                    estimatedStudentCount: this.estimatedStudentCount === ''
                        ? null
                        : Number(this.estimatedStudentCount),
                    // Le plan d'abonnement n'est plus saisi : le serveur le déduit de ce profil.
                    ownership: this.ownership,
                    cycleProfile: this.cycleProfile,
                    sizeTier: this.needsSize ? this.sizeTier : null,
                    website: this.website
                });

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
