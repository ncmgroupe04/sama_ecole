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

    /**
     * Montant en francs CFA : entier, milliers séparés, jamais de décimale — la monnaie n'en a pas.
     *
     * Miroir des documents PDF (`FormatMoney` de SamaEcole.Infrastructure.Documents : reçu
     * d'inscription, reçu de caisse, rapport de clôture, avis d'impayé), qui écrivent
     * `ToString("#,##0").Replace(",", " ")`. C'est la raison du séparateur choisi ici :
     *
     *   - `Intl.NumberFormat('fr-FR')` — la forme recopiée jusqu'ici dans huit scripts de vue —
     *     insère une ESPACE FINE INSÉCABLE (U+202F). Le PDF, lui, insère une espace ordinaire. Le
     *     même montant s'écrivait donc plus serré à l'écran que sur le reçu remis au parent.
     *   - Une espace ORDINAIRE réaligne l'écran sur le PDF mais autorise le navigateur à couper
     *     « 1 250 000 FCFA » en fin de ligne, au milieu du nombre.
     *
     *   D'où l'espace INSÉCABLE (U+00A0) : même largeur apparente que celle du PDF, et le montant
     *   reste insécable dans un tableau étroit.
     *
     * Une valeur absente vaut zéro : une cellule financière vide se lit comme une donnée manquante,
     * alors qu'un solde non renseigné vaut bien 0 FCFA côté API.
     */
    function formatFCFA(amount) {
        // Échappement explicite plutôt que le caractère lui-même : une espace insécable et une espace
        // ordinaire sont indiscernables dans un éditeur, et la première se fait « corriger » en la
        // seconde au premier reformatage automatique du fichier.
        const NBSP = '\u00A0';
        const value = Number(amount) || 0;
        const grouped = Math.round(value)
            .toString()
            .replace(/\B(?=(\d{3})+(?!\d))/g, NBSP);
        return `${grouped}${NBSP}FCFA`;
    }

    window.formatSenegalPhone = formatSenegalPhone;
    window.formatSenegalPhoneOr = formatSenegalPhoneOr;
    window.formatFCFA = formatFCFA;

    // Exposés à Alpine comme magics $phone et $money : `x-text="$money(row.amount)"` dans les vues,
    // sans avoir à câbler la fonction dans chaque composant.
    document.addEventListener('alpine:init', () => {
        Alpine.magic('phone', () => formatSenegalPhoneOr);
        Alpine.magic('money', () => formatFCFA);
    });
})();
