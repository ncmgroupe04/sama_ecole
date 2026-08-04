/**
 * Formatage d'AFFICHAGE partagé — chargé par _Layout, donc disponible dans toutes les vues.
 *
 * Miroir de SamaEcole.Application.Common.PhoneFormatter (C#) : mêmes règles, mêmes limites. Les deux
 * doivent évoluer ensemble — un tableau HTML et le PDF qu'on en tire afficheraient sinon deux
 * écritures différentes du même numéro.
 */
(() => {
    'use strict';

    const COUNTRY_CODE = '221';
    const NATIONAL_LENGTH = 9;
    const GROUPS = [2, 3, 2, 2];

    /**
     * « 770000000 » → « 77 000 00 00 ». Tout ce qui sort du gabarit sénégalais (9 chiffres, groupés
     * 2-3-2-2, indicatif +221 optionnel) est renvoyé TEL QUEL : mieux vaut un affichage brut qu'un
     * découpage inventé sur un numéro étranger ou incomplet, qui aurait l'apparence d'un vrai numéro.
     */
    function formatSenegalPhone(phone) {
        if (phone === null || phone === undefined) return phone;

        const trimmed = String(phone).trim();
        if (trimmed === '') return phone;

        // Un « + » ailleurs qu'en tête n'est pas un indicatif (deux numéros dans un même champ…).
        const hasPlus = trimmed.startsWith('+');
        if (trimmed.indexOf('+', hasPlus ? 1 : 0) >= 0) return phone;

        // Séparateurs tolérés à la saisie ; toute lettre ou symbole en dehors sort du gabarit.
        if (/[^\d\s.\-/()+‑]/.test(trimmed)) return phone;

        const digits = trimmed.replace(/\D/g, '');
        let national = digits;
        let isInternational = false;

        // L'ordre compte : « 00221… » commence aussi par « 221 » une fois les zéros retirés.
        if (digits.startsWith('00' + COUNTRY_CODE)) {
            national = digits.slice(2 + COUNTRY_CODE.length);
            isInternational = true;
        } else if (digits.startsWith(COUNTRY_CODE) && digits.length === COUNTRY_CODE.length + NATIONAL_LENGTH) {
            national = digits.slice(COUNTRY_CODE.length);
            isInternational = true;
        }

        if (national.length !== NATIONAL_LENGTH) return phone;

        const parts = [];
        let offset = 0;
        for (const size of GROUPS) {
            parts.push(national.substr(offset, size));
            offset += size;
        }

        const grouped = parts.join(' ');
        return (isInternational || hasPlus) ? `+${COUNTRY_CODE} ${grouped}` : grouped;
    }

    /** Variante pour les tableaux : repli explicite quand aucun numéro n'est enregistré. */
    function formatSenegalPhoneOr(phone, fallback = '—') {
        if (phone === null || phone === undefined || String(phone).trim() === '') return fallback;
        return formatSenegalPhone(phone);
    }

    window.formatSenegalPhone = formatSenegalPhone;
    window.formatSenegalPhoneOr = formatSenegalPhoneOr;

    // Exposé à Alpine comme magic $phone : `x-text="$phone(teacher.phone)"` dans les vues, sans avoir
    // à câbler la fonction dans chaque composant.
    document.addEventListener('alpine:init', () => {
        Alpine.magic('phone', () => formatSenegalPhoneOr);
    });
})();
