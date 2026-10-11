/**
 * Logique pure de l'écran Internat (/internat) — partagée par ses onglets (Pavillons, Occupation, Pensionnaires, Sorties, Pointage).
 *
 * Aucune dépendance au DOM ni à Alpine : jauge d'occupation, libellés, corps de requête, validation de confort et traduction des codes
 * d'erreur. Testée sans navigateur (tests/js/internat-helpers.test.mjs).
 *
 * LE SERVEUR RESTE LE JUGE. La validation d'ici n'est qu'un confort (message avant l'aller-retour) : FluentValidation et les règles métier
 * (`RESOURCE_IN_USE`, `BED_UNAVAILABLE`, rôles, module Internat) sont celles de l'API, qui répond 422/409/403. Les bornes reprennent celles des
 * validateurs C# (CreateDormitoryCommandValidator, CreateDormitoryRoomCommand) ; en cas de divergence, c'est le serveur qui fait foi.
 */
window.internatHelpers = (() => {
    // ------------------------------------------------------------------ Jauge d'occupation

    /** Pourcentage d'occupation, borné à 0–100. Capacité nulle ou inconnue : 0 (jamais de division par zéro). */
    function occupancyPercent(occupied, capacity) {
        const used = Number(occupied) || 0;
        const total = Number(capacity) || 0;
        if (total <= 0 || used <= 0) return 0;
        return Math.min(100, Math.round((used / total) * 100));
    }

    /**
     * Couleur de la jauge. Mêmes seuils que l'écran historique, dans le vocabulaire de teinte de StatCardTagHelper : vide (slate), en
     * remplissage (emerald), proche de la capacité — 80 % et plus (amber), complet (rose).
     */
    function occupancyColor(occupied, capacity) {
        const used = Number(occupied) || 0;
        const total = Number(capacity) || 0;
        if (used <= 0) return 'bg-slate-300';
        if (total > 0 && used >= total) return 'bg-rose-500';
        if (total > 0 && used / total >= 0.8) return 'bg-amber-500';
        return 'bg-emerald-500';
    }

    // ------------------------------------------------------------------ Onglets

    /** Onglets de l'écran, dans l'ordre d'affichage. D'autres s'ajoutent aux tâches suivantes du lot G (aucune : les cinq onglets sont en place). */
    const TABS = [
        { key: 'occupation', label: 'Occupation' },
        { key: 'pavillons', label: 'Pavillons' },
        { key: 'pensionnaires', label: 'Pensionnaires' },
        { key: 'sorties', label: 'Sorties' },
        { key: 'pointage', label: 'Pointage' }
    ];

    /** Onglet désigné par le fragment d'URL (`#pavillons`), `occupation` par défaut : un fragment inconnu ne casse jamais l'écran. */
    function tabFromHash(hash) {
        const wanted = String(hash || '').replace(/^#/, '').toLowerCase();
        const found = TABS.find((t) => t.key === wanted);
        return found ? found.key : TABS[0].key;
    }

    // ------------------------------------------------------------------ Rechargement à l'ouverture d'un onglet

    /** Événement (sur window) annonçant l'onglet qui vient d'être affiché : chaque composant décide s'il doit relire ses données. */
    const TAB_SHOWN_EVENT = 'internat-tab-shown';

    /** Données chargées depuis moins que cela : pas relues (évite un double chargement à l'ouverture de la page et aux allers-retours rapides). */
    const REFRESH_MIN_GAP_MS = 2000;

    function announceTabShown(key) {
        const event = typeof CustomEvent === 'function' ? new CustomEvent(TAB_SHOWN_EVENT, { detail: { key } }) : { type: TAB_SHOWN_EVENT, detail: { key } };
        window.dispatchEvent(event);
    }

    /**
     * Faut-il relire les données de l'onglet `tabKey` maintenant que `shownKey` vient d'être affiché ? Oui seulement pour SON onglet, s'il n'est pas `busy`
     * (modale ouverte, enregistrement en cours, saisie non enregistrée : relire détruirait l'état de l'utilisateur) et si ses données ne viennent pas d'être
     * chargées. Les onglets ne chargeaient qu'à l'ouverture de la page : une écriture faite dans un autre onglet n'y apparaissait pas (recette navigateur).
     */
    function shouldRefreshOnShow({ tabKey, shownKey, busy, loadedAt, now }) {
        if (shownKey !== tabKey || busy) return false;
        return !loadedAt || now - loadedAt >= REFRESH_MIN_GAP_MS;
    }

    // ------------------------------------------------------------------ Libellés

    /**
     * Genres proposables à la saisie. `Mixte` n'existe que pour la reprise de données (spec Q2) : l'API le refuse à la création et à la
     * modification, l'écran ne le propose donc jamais.
     */
    const GENDER_CHOICES = [
        { value: 'Garcons', label: 'Garçons' },
        { value: 'Filles', label: 'Filles' }
    ];

    function genderLabel(gender) {
        if (gender === 'Garcons') return 'Garçons';
        if (gender === 'Filles') return 'Filles';
        // Un pavillon repris est affichable (le Directeur doit pouvoir le qualifier), mais ce n'est pas un choix de saisie.
        if (gender === 'Mixte') return 'Mixte (à qualifier)';
        return '';
    }

    const BED_STATUS_LABELS = { Available: 'Disponible', Occupied: 'Occupé', Maintenance: 'En maintenance' };

    function bedStatusLabel(status) {
        return BED_STATUS_LABELS[status] || status || '';
    }

    // ------------------------------------------------------------------ Corps de requête

    function blankToNull(value) {
        const text = typeof value === 'string' ? value.trim() : '';
        return text === '' ? null : text;
    }

    /**
     * Corps de POST/PUT /api/v1/boarding/dormitories. La modification porte `rowVersion` (verrou optimiste, jeton xmin) ; un jeton à 0 est un
     * jeton valide. Le compte surveillant lié (`supervisorUserId`) est conservé tel quel s'il existe : l'écran ne le modifie pas (Q7 : texte
     * libre en V1, liaison optionnelle ailleurs).
     */
    function buildDormitoryBody(form, rowVersion) {
        const body = {
            name: String(form.name || '').trim(),
            gender: form.gender,
            supervisorName: blankToNull(form.supervisorName),
            supervisorPhone: blankToNull(form.supervisorPhone),
            supervisorUserId: form.supervisorUserId || null,
            notes: blankToNull(form.notes)
        };
        if (rowVersion !== undefined && rowVersion !== null) body.rowVersion = rowVersion;
        return body;
    }

    /** Corps de POST /api/v1/boarding/rooms : crée la chambre ET ses lits 1..bedCount, tout ou rien. */
    function buildRoomBody(dormitoryId, name, bedCount) {
        return { dormitoryId, name: String(name || '').trim(), bedCount: Number(bedCount) };
    }

    // ------------------------------------------------------------------ Validation de confort

    function tooLong(value, max) {
        return typeof value === 'string' && value.length > max;
    }

    /** Erreurs par champ ; objet vide = valide. */
    function validateDormitory(form) {
        const errors = {};
        const name = String(form.name || '').trim();

        if (name === '') errors.name = 'Le nom du pavillon est obligatoire.';
        else if (name.length > 100) errors.name = 'Le nom ne doit pas dépasser 100 caractères.';

        if (!GENDER_CHOICES.some((g) => g.value === form.gender)) errors.gender = 'Choisissez Garçons ou Filles.';

        if (tooLong(form.supervisorName, 150)) errors.supervisorName = 'Le nom du surveillant ne doit pas dépasser 150 caractères.';
        if (tooLong(form.supervisorPhone, 30)) errors.supervisorPhone = 'Le téléphone ne doit pas dépasser 30 caractères.';
        if (tooLong(form.notes, 1000)) errors.notes = 'Les notes ne doivent pas dépasser 1000 caractères.';

        return errors;
    }

    /** Une chambre compte entre 1 et 40 lits (entiers). */
    function validateRoom(form) {
        const errors = {};
        const name = String(form.name || '').trim();
        const count = Number(form.bedCount);

        if (name === '') errors.name = 'Le nom de la chambre est obligatoire.';
        else if (name.length > 100) errors.name = 'Le nom ne doit pas dépasser 100 caractères.';

        if (!Number.isInteger(count) || count < 1 || count > 40) errors.bedCount = 'Une chambre compte entre 1 et 40 lits.';

        return errors;
    }

    // ------------------------------------------------------------------ Sorties

    const LEAVE_STATUS_LABELS = { Pending: 'À venir', Active: 'En cours', Overdue: 'En retard', Returned: 'Rentré' };
    const LEAVE_REASONS = [
        { value: 'Weekend', label: 'Week-end' },
        { value: 'Sante', label: 'Santé' },
        { value: 'Famille', label: 'Famille' },
        { value: 'Autre', label: 'Autre' }
    ];

    /** Statut CALCULÉ par le serveur (jamais stocké), affiché en français. Un statut inconnu reste lisible tel quel. */
    function leaveStatusLabel(status) {
        return LEAVE_STATUS_LABELS[status] || status || '';
    }

    function leaveReasonLabel(reason) {
        const found = LEAVE_REASONS.find((r) => r.value === reason);
        return found ? found.label : (reason || '');
    }

    /** Jour courant, local, au format yyyy-MM-dd (isolé ici pour pouvoir être figé dans les tests). */
    function today() {
        return isoDate(new Date());
    }

    // ------------------------------------------------------------------ PDF

    /** Date au format yyyy-MM-dd attendu par l'API : une chaîne déjà conforme, ou un objet Date lu avec ses composantes LOCALES (toISOString décalerait le jour). */
    function isoDate(value) {
        if (Object.prototype.toString.call(value) === '[object Date]' && !Number.isNaN(value.getTime())) {
            const pad = (n) => String(n).padStart(2, '0');
            return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
        }
        return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : '';
    }

    /** GET /boarding/boarders/{id}/pdf — fiche individuelle. Aucun paramètre : le masquage de la fiche médicale est décidé par le serveur, selon le rôle. */
    function boarderPdfUrl(boarderId) {
        return boarderId ? `/api/v1/boarding/boarders/${boarderId}/pdf` : '';
    }

    /** GET /boarding/dormitories/{id}/register-pdf?date= — registre d'une nuit. Sans date valide, le serveur prend aujourd'hui. */
    function registerPdfUrl(dormitoryId, date) {
        if (!dormitoryId) return '';
        const day = isoDate(date);
        return `/api/v1/boarding/dormitories/${dormitoryId}/register-pdf${day ? `?date=${day}` : ''}`;
    }

    /** Nom de téléchargement ASCII : accents ramenés à leur lettre, espaces en tirets, tout autre caractère retiré (jamais de « ../ »). */
    function pdfFileName(prefix, ...parts) {
        const safe = parts
            .map((part) => String(part || '')
                .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
                .replace(/\s+/g, '-')
                .replace(/[^A-Za-z0-9_-]/g, ''))
            .filter(Boolean);
        return [prefix, ...safe].join('-') + '.pdf';
    }

    // ------------------------------------------------------------------ Codes d'erreur

    /** Messages actionnables par code métier de l'API (Volume 4 §0.3 et §31). */
    const ERROR_MESSAGES = {
        RESOURCE_IN_USE: "Suppression impossible : cet élément contient encore des éléments actifs (chambres, lits ou pensionnaires). Videz-le d'abord.",
        ACTIVE_ENTITY_CONFLICT: 'Un élément actif porte déjà ce nom.',
        ARCHIVED_ENTITY_EXISTS: 'Un élément supprimé porte déjà ce nom : restaurez-le depuis les éléments supprimés plutôt que de le recréer.',
        PARENT_ENTITY_ARCHIVED: "Restauration impossible : l'élément parent est supprimé. Restaurez-le d'abord (le pavillon, puis la chambre).",
        CONCURRENCY_CONFLICT: 'Cet élément a été modifié par une autre personne. Les données ont été rechargées : vérifiez puis recommencez.',
        LEAVE_ALREADY_OPEN: "Ce pensionnaire a déjà une sortie en cours : enregistrez d'abord son retour.",
        LEAVE_ALREADY_RETURNED: 'Le retour de cette sortie est déjà enregistré. Le registre a été rechargé.',
        ATTENDANCE_CONFLICT: "Le pointage de cette nuit a été modifié par une autre personne. La feuille d'appel a été rechargée : vérifiez puis recommencez.",
        BED_UNAVAILABLE: "Ce lit vient d'être attribué à un autre pensionnaire.",
        MODULE_DISABLED: "Le module Internat n'est pas activé pour votre établissement."
    };

    /**
     * Message à afficher pour une erreur d'API : le code métier d'abord, puis le message du serveur (`window.api.toMessage`, qui renvoie une
     * chaîne vide pour une erreur déjà montrée par la modale « Accès refusé »), puis le repli. Fonctionne sans `window.api` (tests).
     */
    function errorMessage(error, fallback) {
        if (error && error.handledGlobally) return '';
        if (error && error.code && ERROR_MESSAGES[error.code]) return ERROR_MESSAGES[error.code];

        const api = window.api;
        if (api && typeof api.toMessage === 'function') return api.toMessage(error, fallback);

        return (error && error.message) || fallback || '';
    }

    return {
        occupancyPercent, occupancyColor,
        TABS, tabFromHash, TAB_SHOWN_EVENT, REFRESH_MIN_GAP_MS, announceTabShown, shouldRefreshOnShow,
        GENDER_CHOICES, genderLabel, bedStatusLabel,
        buildDormitoryBody, buildRoomBody,
        validateDormitory, validateRoom,
        LEAVE_REASONS, leaveStatusLabel, leaveReasonLabel, today,
        isoDate, boarderPdfUrl, registerPdfUrl, pdfFileName,
        ERROR_MESSAGES, errorMessage
    };
})();
