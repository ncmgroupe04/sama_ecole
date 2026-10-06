/*
 * Aide partagée des écrans d'import de masse (élèves — students.js, enseignants — teachers.js) pour LIRE et
 * TRANSMETTRE les erreurs de l'aperçu (dryRun=true).
 *
 * L'aperçu colore déjà la cellule fautive et porte le message en infobulle ; sur un fichier de plusieurs
 * centaines de lignes, retrouver chaque erreur revient à défiler puis survoler une à une. Cette aide en tire
 * une liste plate « Ligne / Champ / Valeur saisie / Message », un filtre « lignes en erreur seulement » et un
 * export CSV à renvoyer à la personne qui a saisi le fichier.
 *
 * PURE côté logique (aucun accès à Alpine ni à l'API) pour se tester seule — voir
 * tests/js/import-errors.test.mjs. Seul download() touche au navigateur.
 *
 * Format de ligne attendu (ImportStudentsRowResult / ImportTeachersRowResult) :
 *   { rowNumber, isValid, <champ>: valeur brute, fieldErrors: { <champ>: message } }
 */
(() => {
    /** Octet d'ordre (BOM) : sans lui, Excel ouvre un CSV UTF-8 en ANSI et abîme les accents (« Thiès »). */
    const BOM = '﻿';

    /** Point-virgule : séparateur par défaut d'Excel en région francophone (la virgule y est le décimal). */
    const SEPARATOR = ';';

    const HEADER = ['Ligne', 'Champ', 'Valeur saisie', 'Message'];

    /** Vrai si la ligne porte au moins une erreur de champ (ou est marquée invalide par le serveur). */
    function rowHasErrors(row) {
        if (!row) return false;
        if (row.isValid === false) return true;
        return !!row.fieldErrors && Object.keys(row.fieldErrors).length > 0;
    }

    /** Lignes en erreur uniquement, dans leur ordre d'origine. */
    function invalidRows(rows) {
        return (rows || []).filter(rowHasErrors);
    }

    /**
     * Liste plate des erreurs, triée par numéro de ligne puis dans l'ordre des colonnes de l'écran (l'ordre de
     * `labels`) — un champ inconnu de `labels` passe en dernier, avec son nom technique pour libellé : une
     * erreur n'est jamais perdue faute d'étiquette.
     *
     * @param {object[]} rows    lignes de l'aperçu
     * @param {object}   labels  { champ: 'Libellé de la colonne' }, dans l'ordre d'affichage
     */
    function entries(rows, labels) {
        const order = Object.keys(labels || {});
        const rank = (field) => {
            const index = order.indexOf(field);
            return index === -1 ? order.length : index;
        };

        const result = [];
        for (const row of rows || []) {
            const fieldErrors = (row && row.fieldErrors) || {};
            Object.keys(fieldErrors)
                .sort((a, b) => rank(a) - rank(b))
                .forEach((field) => {
                    const raw = row[field];
                    result.push({
                        rowNumber: row.rowNumber,
                        field,
                        label: (labels && labels[field]) || field,
                        value: raw === null || raw === undefined ? '' : String(raw),
                        message: String(fieldErrors[field])
                    });
                });
        }

        return result.sort((a, b) => a.rowNumber - b.rowNumber);
    }

    /**
     * Une cellule CSV. Entre guillemets dès qu'elle contient le séparateur, un guillemet ou un saut de ligne
     * (guillemets doublés). Une valeur qui commence par = + - @ (ou une tabulation / un retour chariot) est
     * précédée d'une apostrophe : sans cela, Excel l'exécuterait comme une FORMULE — la « valeur saisie » est
     * le texte d'un fichier tiers, jamais une donnée de confiance (injection CSV).
     */
    function cell(value) {
        let text = value === null || value === undefined ? '' : String(value);

        if (/^[=+\-@\t\r]/.test(text)) {
            text = `'${text}`;
        }

        return /[;"\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
    }

    /** Rapport CSV complet : BOM, en-tête, une ligne par erreur, fins de ligne CRLF (convention Excel). */
    function toCsv(errorEntries) {
        const lines = [HEADER.map(cell).join(SEPARATOR)];

        for (const e of errorEntries || []) {
            lines.push([e.rowNumber, e.label, e.value, e.message].map(cell).join(SEPARATOR));
        }

        return BOM + lines.join('\r\n') + '\r\n';
    }

    /** « Rapport-Erreurs-Import-Eleves-2026-10-06.csv » — date LOCALE (jamais le décalage UTC d'un fuseau). */
    function fileName(subject, date = new Date()) {
        const two = (n) => String(n).padStart(2, '0');
        const day = `${date.getFullYear()}-${two(date.getMonth() + 1)}-${two(date.getDate())}`;
        return `Rapport-Erreurs-Import-${subject}-${day}.csv`;
    }

    /** Télécharge `content` sous `name` — même mécanique blob + lien que les autres téléchargements de l'application. */
    function download(name, content) {
        const blob = new Blob([content], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    }

    window.importErrors = { rowHasErrors, invalidRows, entries, toCsv, fileName, download };
})();
