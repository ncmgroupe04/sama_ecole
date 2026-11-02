/**
 * Logique pure du pointage (feuille d'appel d'un pavillon, d'une classe ou d'une Halqa) — partagée par l'onglet « Pointage » de /internat
 * (internat-attendance.js) et par l'appel de la Halqa de l'Oustaz (halqa-roll.js). Aucune dépendance au DOM ni à Alpine : testée sans navigateur
 * (tests/js/internat-attendance.test.mjs, tests/js/attendance-sessions.test.mjs).
 *
 * Enjeux : (1) un pensionnaire en permission ne peut JAMAIS être coché présent (le serveur le refuserait : BOARDER_ON_LEAVE) ; (2) seules les lignes
 * décidées ET modifiées partent — une ligne déjà enregistrée n'est pas renvoyée ; (3) un résultat mixte montre les rejets sans masquer les succès ;
 * (4) une date future est refusée avant tout appel ; (5) changer de groupe ou de jour ne jette pas une saisie non enregistrée sans le dire.
 *
 * Deux formes de feuille, une seule logique : la feuille d'un PAVILLON (lignes identifiées par `boarderId`, le séjour) et la feuille d'une
 * CLASSE ou d'une HALQA (lignes identifiées par `studentId`). `toGroupLines` ramène la seconde à la forme de la première : la clé de travail
 * `boarderId` y porte l'identifiant de l'élève, `studentId` aussi pour l'envoi.
 */
window.attendanceLogic = (() => {
    const MAX_NOTE = 300;
    /** Même note que le serveur pose à un absent en permission quand l'opérateur n'en saisit pas (AttendanceRules.OnLeaveNote). */
    const ON_LEAVE_NOTE = 'En permission';

    const REJECTION_MESSAGES = {
        NOT_IN_DORMITORY: "Ce pensionnaire n'a pas (ou plus) de lit dans ce pavillon : rechargez la feuille d'appel.",
        NOT_IN_GROUP: "Cet élève ne fait pas (ou plus) partie de ce groupe : rechargez la feuille d'appel.",
        DUPLICATE_ENTRY: 'Ce pensionnaire figurait deux fois dans la saisie : la première ligne a été retenue.',
        BOARDER_ON_LEAVE: 'Ce pensionnaire est en sortie cette nuit-là : il ne peut être pointé qu\'absent.'
    };

    function blankToNull(value) {
        const text = typeof value === 'string' ? value.trim() : '';
        return text === '' ? null : text;
    }

    /** Lignes de travail d'une feuille : la valeur enregistrée (`base`) sert à savoir ce qui a changé. */
    function toLines(sheet) {
        return sheet.lines.map((l) => ({
            boarderId: l.boarderId,
            studentName: l.studentName,
            matricule: l.matricule,
            roomName: l.roomName,
            bedNumber: l.bedNumber,
            onLeave: !!l.onLeave,
            recorded: !!l.recorded,
            isPresent: l.isPresent === undefined ? null : l.isPresent,
            note: l.note ?? null,
            base: { recorded: !!l.recorded, isPresent: l.isPresent === undefined ? null : l.isPresent, note: l.note ?? null },
            rejection: ''
        }));
    }

    /**
     * Lignes de travail d'une feuille de CLASSE ou de HALQA (GET attendance/sheet). Même forme que `toLines` : `boarderId` sert de clé, il vaut
     * ici l'identifiant de l'élève. `detail` (régime) remplace « chambre · lit ».
     */
    function toGroupLines(sheet) {
        return sheet.lines.map((l) => ({
            boarderId: l.studentId,
            studentId: l.studentId,
            studentName: l.studentName,
            studentNameAr: l.studentNameAr || null,
            matricule: l.matricule,
            roomName: l.detail || '',
            bedNumber: null,
            onLeave: !!l.onLeave,
            recorded: !!l.recorded,
            isPresent: l.isPresent === undefined ? null : l.isPresent,
            note: l.note ?? null,
            base: { recorded: !!l.recorded, isPresent: l.isPresent === undefined ? null : l.isPresent, note: l.note ?? null },
            rejection: ''
        }));
    }

    /** Pointe une ligne. Un pensionnaire en permission ne peut pas être « présent » : refus (faux), la ligne reste inchangée. */
    function setPresence(line, present) {
        if (line.onLeave && present) return false;
        line.isPresent = present;
        return true;
    }

    /** Vrai si la ligne doit partir : décidée, et jamais enregistrée ou différente de ce qui est enregistré. */
    function isDirty(line) {
        if (line.isPresent === null || line.isPresent === undefined) return false;
        if (!line.base.recorded) return true;
        return line.isPresent !== line.base.isPresent || blankToNull(line.note) !== blankToNull(line.base.note);
    }

    /**
     * Vrai si l'UTILISATEUR a changé quelque chose depuis le chargement (ou depuis le dernier enregistrement). Différent de isDirty : une permission
     * pré-remplie par le serveur est « à envoyer » dès l'ouverture, sans que personne n'ait rien saisi — elle ne doit pas déclencher la demande d'abandon.
     */
    function hasUnsavedInput(lines) {
        return lines.some((l) => l.isPresent !== l.base.isPresent || blankToNull(l.note) !== blankToNull(l.base.note));
    }

    /** Corps minimal de POST : seules les lignes à envoyer. */
    function dirtyEntries(lines) {
        return lines.filter(isDirty).map((l) => ({ boarderId: l.boarderId, isPresent: l.isPresent, note: blankToNull(l.note) }));
    }

    /** Corps minimal de POST d'une feuille de classe ou de Halqa : seules les lignes à envoyer, par `studentId`. */
    function dirtyGroupEntries(lines) {
        return lines.filter(isDirty).map((l) => ({ studentId: l.studentId, isPresent: l.isPresent, note: blankToNull(l.note) }));
    }

    /** « Tout présent » : ne touche ni les décisions déjà prises ni les pensionnaires en permission. */
    function markAllPresent(lines) {
        lines.forEach((l) => {
            if (!l.onLeave && (l.isPresent === null || l.isPresent === undefined)) l.isPresent = true;
        });
    }

    function validateDate(date, today) {
        if (!date) return 'Indiquez la date de la nuit à pointer.';
        if (date > today) return 'Le pointage ne peut pas porter sur une date future.';
        return '';
    }

    function validateNotes(lines) {
        return lines.some((l) => typeof l.note === 'string' && l.note.length > MAX_NOTE) ? `Une note ne doit pas dépasser ${MAX_NOTE} caractères.` : '';
    }

    function rejectionMessage(code, serverMessage) {
        return REJECTION_MESSAGES[code] || serverMessage || 'Ligne refusée par le serveur.';
    }

    /** Reporte le résultat du serveur sur les lignes : succès = enregistrée et propre, rejet = message conservé et ligne toujours à traiter. */
    function applyResult(lines, result) {
        result.lines.forEach((r) => {
            const line = lines.find((l) => l.boarderId === (r.boarderId ?? r.studentId));
            if (!line) return;
            if (r.outcome === 'Saved') {
                // Le serveur pose « En permission » à un absent en sortie sans note : on l'aligne pour que la ligne ne paraisse pas modifiée.
                if (line.isPresent === false && line.onLeave && blankToNull(line.note) === null) line.note = ON_LEAVE_NOTE;
                line.recorded = true;
                line.base = { recorded: true, isPresent: line.isPresent, note: line.note };
                line.rejection = '';
            } else {
                line.rejection = rejectionMessage(r.code, r.message);
            }
        });
    }

    function counts(lines) {
        return {
            present: lines.filter((l) => l.isPresent === true).length,
            absent: lines.filter((l) => l.isPresent === false).length,
            pending: lines.filter((l) => l.isPresent === null || l.isPresent === undefined).length,
            dirty: lines.filter(isDirty).length
        };
    }

    // ------------------------------------------------------------------ Sessions de pointage

    /** Public d'une session, tel que l'écran de configuration le présente (valeurs de l'API : AttendanceAudience). */
    const AUDIENCES = [
        { value: 'Interns', label: 'Internes, par pavillon', group: 'Pavillon' },
        { value: 'HalfBoarders', label: 'Demi-pensionnaires, par classe', group: 'Classe' },
        { value: 'AllBoarders', label: 'Tous les pensionnaires, par classe', group: 'Classe' },
        { value: 'Halqa', label: "Élèves d'une Halqa, par Oustaz", group: 'Oustaz' }
    ];

    /** Jours ISO : 1 = lundi … 7 = dimanche (comme l'API). */
    const DAYS = [
        { value: 1, short: 'Lun', long: 'Lundi' }, { value: 2, short: 'Mar', long: 'Mardi' }, { value: 3, short: 'Mer', long: 'Mercredi' },
        { value: 4, short: 'Jeu', long: 'Jeudi' }, { value: 5, short: 'Ven', long: 'Vendredi' }, { value: 6, short: 'Sam', long: 'Samedi' },
        { value: 7, short: 'Dim', long: 'Dimanche' }
    ];

    function audienceLabel(audience) {
        const found = AUDIENCES.find((a) => a.value === audience);
        return found ? found.label : '';
    }

    /** Nom du groupe choisi pour ce public : « Pavillon », « Classe » ou « Oustaz ». */
    function groupLabel(audience) {
        const found = AUDIENCES.find((a) => a.value === audience);
        return found ? found.group : 'Groupe';
    }

    /** Les internes se pointent par pavillon (feuille d'origine) ; tous les autres publics par un groupe (classe ou Oustaz). */
    function isGroupAudience(audience) {
        return audience !== 'Interns';
    }

    /** Résumé des jours : « Tous les jours », « Lun – Ven », « Lun, Mer, Sam »… */
    function daysSummary(days) {
        const sorted = [...new Set((days || []).map(Number))].filter((d) => d >= 1 && d <= 7).sort((a, b) => a - b);
        if (sorted.length === 0) return 'Aucun jour';
        if (sorted.length === 7) return 'Tous les jours';
        const short = (d) => DAYS[d - 1].short;
        const consecutive = sorted.every((d, i) => i === 0 || d === sorted[i - 1] + 1);
        if (consecutive && sorted.length >= 3) return `${short(sorted[0])} – ${short(sorted[sorted.length - 1])}`;
        return sorted.map(short).join(', ');
    }

    /** Jour ISO (1 à 7) d'une date AAAA-MM-JJ, sans passer par le fuseau du navigateur. */
    function isoDay(date) {
        const [y, m, d] = String(date).split('-').map(Number);
        const weekday = new Date(Date.UTC(y, m - 1, d)).getUTCDay();
        return weekday === 0 ? 7 : weekday;
    }

    /** Vrai si la date est un jour que la session ne couvre pas (information, jamais un blocage : le serveur accepte). */
    function outsideDays(days, date) {
        if (!date || !Array.isArray(days) || days.length === 0) return false;
        return !days.map(Number).includes(isoDay(date));
    }

    /** Mois AAAA-MM d'une date AAAA-MM-JJ. */
    function monthOf(date) {
        return String(date || '').slice(0, 7);
    }

    /** Libellé « octobre 2026 » d'un mois AAAA-MM. */
    function monthLabel(month) {
        const [y, m] = String(month).split('-').map(Number);
        if (!y || !m) return '';
        const names = ['janvier', 'février', 'mars', 'avril', 'mai', 'juin', 'juillet', 'août', 'septembre', 'octobre', 'novembre', 'décembre'];
        return `${names[m - 1]} ${y}`;
    }

    /** Totaux d'un récapitulatif mensuel : une somme par colonne. */
    function summaryTotals(rows) {
        return (rows || []).reduce(
            (t, r) => ({ present: t.present + r.present, absent: t.absent + r.absent, onLeave: t.onLeave + r.onLeave, notRecorded: t.notRecorded + r.notRecorded }),
            { present: 0, absent: 0, onLeave: 0, notRecorded: 0 });
    }

    /** Contrôle de confort d'une session avant l'envoi (le serveur reste le juge : FluentValidation). Message, ou chaîne vide. */
    function validateSession(form) {
        const name = typeof form.name === 'string' ? form.name.trim() : '';
        if (!name) return 'Donnez un nom à la session.';
        if (name.length > 60) return 'Le nom ne doit pas dépasser 60 caractères.';
        if (!AUDIENCES.some((a) => a.value === form.audience)) return 'Choisissez à qui s\'adresse la session.';
        if (!Array.isArray(form.days) || form.days.length === 0) return 'Choisissez au moins un jour.';
        if (typeof form.timeLabel === 'string' && form.timeLabel.trim().length > 40) return 'Le repère horaire ne doit pas dépasser 40 caractères.';
        return '';
    }

    return {
        MAX_NOTE, ON_LEAVE_NOTE, AUDIENCES, DAYS, toLines, toGroupLines, dirtyGroupEntries, setPresence, isDirty, hasUnsavedInput, dirtyEntries, markAllPresent, validateDate, validateNotes, rejectionMessage, applyResult, counts,
        audienceLabel, groupLabel, isGroupAudience, daysSummary, isoDay, outsideDays, monthOf, monthLabel, summaryTotals, validateSession
    };
})();
