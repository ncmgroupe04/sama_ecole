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
const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const SENEGAL_PHONE_REGEX = /^(?:\+221|00221)?\s?(?:77|76|78|70|75|33)(?:\s?\d){7}$/;
const UNSAFE_TEXT_REGEX = /[<>]|javascript\s*:|&#/i;
const FORBIDDEN_PASSWORD_SEQUENCES = ['123456', 'azerty', 'qwerty', 'password', 'motdepasse', 'abcdef'];

const isSafeText = (value) => !UNSAFE_TEXT_REGEX.test(value);

/** Reproduit PasswordPolicy.Validate (C#) : mêmes règles, mêmes messages. */
function passwordPolicyErrors(password, personalTerms) {
    const errors = [];

    if (password.length < 12) errors.push('Le mot de passe doit contenir au moins 12 caractères.');
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

        const fallback = new Error(`Erreur HTTP ${response.status}`);
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
        requestedPlan: 'Standard',

        // Honeypot : DOIT rester vide. Lié au champ leurre `website`.
        website: '',

        isSubmitting: false,
        error: null,
        errors: {},
        trackingReference: null,

        // Pré-remplit la formule quand on arrive depuis une carte tarifaire de la vitrine
        // (/inscription?plan=Premium) — une simple commodité d'affichage, jamais fait confiance
        // côté serveur : CreateRegistrationRequestCommand revalide requestedPlan indépendamment.
        init() {
            const plan = new URLSearchParams(window.location.search).get('plan');
            if (['Primaire', 'Standard', 'Premium'].includes(plan)) {
                this.requestedPlan = plan;
            }
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
            } else if (!isSafeText(this.directorFullName)) {
                errors.directorfullname = 'Le nom complet contient des caractères interdits.';
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

            this.errors = errors;
            return Object.keys(errors).length === 0;
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
                    requestedPlan: this.requestedPlan,
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
