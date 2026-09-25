/**
 * Dispenses d'une matière obligatoire (motif obligatoire) — logique PURE, sans DOM ni réseau, testable sous
 * `node --test`. Aucune règle métier n'est décidée ici : le serveur (ExemptionRules) reste seul juge ; ce module ne
 * sert qu'à ne pas laisser l'utilisateur composer une demande qu'il refuserait. Le motif peut être médical : il ne
 * quitte jamais l'état du composant (aucun log, aucun stockage).
 */
(function () {
    'use strict';

    /** État initial du formulaire à partir des matières renvoyées par le serveur : case et motif repris. */
    function stateFrom(subjects) {
        return Object.fromEntries((subjects || []).map((s) => [
            s.subjectId,
            { checked: !!s.isExempt, reason: s.reason ?? '' }
        ]));
    }

    /** Les dispenses à envoyer : uniquement les matières cochées, motif nettoyé. Liste vide si aucune case n'est cochée. */
    function payload(state) {
        return Object.entries(state || {})
            .filter(([, entry]) => entry && entry.checked)
            .map(([subjectId, entry]) => ({ subjectId, reason: (entry.reason ?? '').trim() }));
    }

    /** Noms des matières cochées dont le motif est vide : le serveur refuserait l'enregistrement (422). */
    function missingReasons(subjects, state) {
        return (subjects || [])
            .filter((s) => state && state[s.subjectId] && state[s.subjectId].checked
                && !(state[s.subjectId].reason ?? '').trim())
            .map((s) => s.name);
    }

    /** Notes de l'année que ces dispenses masqueraient : somme de `gradeCount` des matières cochées. */
    function hiddenGrades(subjects, state) {
        return (subjects || [])
            .filter((s) => state && state[s.subjectId] && state[s.subjectId].checked)
            .reduce((sum, s) => sum + (s.gradeCount || 0), 0);
    }

    window.subjectExemptions = { stateFrom, payload, missingReasons, hiddenGrades };
})();
