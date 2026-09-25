/**
 * Choix d'options d'un élève (LV2, option scientifique) — logique PURE, sans DOM ni réseau.
 *
 * Partagée par l'écran d'inscription (enrollments.js) et l'onglet « Options » de la fiche élève
 * (students.js) : une seule définition de ce qu'est un groupe, de l'exclusivité et du décompte des notes
 * qu'un choix masquerait. AUCUNE règle métier n'est décidée ici : le serveur (OptionSelectionRules) reste
 * seul juge d'un choix, ce module ne sert qu'à ne pas laisser l'utilisateur composer un choix qu'il
 * refuserait. Le niveau et le groupe sont des textes libres, comparés sans casse ni espaces de bord —
 * exactement comme côté serveur.
 */
(function () {
    'use strict';

    const norm = (value) => (value ?? '').trim().toLowerCase();

    /**
     * Groupes d'options d'un niveau : `[{ key, label, exclusive, subjects: [{ id, name, … }] }]`.
     * Les groupes nommés (exclusifs) d'abord, par libellé ; puis chaque option sans groupe, seule dans son
     * entrée (`exclusive: false`, `label: null`). Une matière obligatoire ou d'un autre niveau n'en fait pas partie.
     */
    function groupsForLevel(subjects, level) {
        const options = (subjects || [])
            .filter((s) => s.isOptional && !s.parentSubjectId && norm(s.level) === norm(level));

        const byGroup = new Map();
        options.filter((s) => norm(s.optionGroup) !== '').forEach((s) => {
            const key = norm(s.optionGroup);
            if (!byGroup.has(key)) byGroup.set(key, { key, label: s.optionGroup.trim(), exclusive: true, subjects: [] });
            byGroup.get(key).subjects.push({ id: s.id, name: s.name });
        });

        const named = [...byGroup.values()]
            .map((g) => ({ ...g, subjects: g.subjects.sort((a, b) => a.name.localeCompare(b.name, 'fr')) }))
            .sort((a, b) => a.label.localeCompare(b.label, 'fr'));

        const free = options
            .filter((s) => norm(s.optionGroup) === '')
            .sort((a, b) => a.name.localeCompare(b.name, 'fr'))
            .map((s) => ({ key: `free:${s.id}`, label: null, exclusive: false, subjects: [{ id: s.id, name: s.name }] }));

        return [...named, ...free];
    }

    const idsOf = (group) => group.subjects.map((s) => s.id);

    /** Ajoute `subjectId` au choix : remplace l'autre choix d'un groupe exclusif, bascule une option libre. */
    function choose(group, selected, subjectId) {
        const current = selected || [];
        if (group.exclusive) {
            return [...current.filter((id) => !idsOf(group).includes(id)), subjectId];
        }
        return current.includes(subjectId) ? current.filter((id) => id !== subjectId) : [...current, subjectId];
    }

    /** Retire de `selected` toutes les matières du groupe. */
    function clearGroup(group, selected) {
        return (selected || []).filter((id) => !idsOf(group).includes(id));
    }

    /** Libellés des groupes exclusifs où rien n'est choisi : l'élève n'y suivra aucune matière. */
    function unchosenGroupLabels(groups, selected) {
        const current = selected || [];
        return groups
            .filter((g) => g.exclusive && !idsOf(g).some((id) => current.includes(id)))
            .map((g) => g.label);
    }

    /** Notes de l'année que ce choix masquerait : somme de `gradeCount` des matières non suivies. */
    function hiddenGradeCount(groups, selected) {
        const current = selected || [];
        return groups
            .flatMap((g) => g.subjects)
            .filter((s) => !current.includes(s.id))
            .reduce((sum, s) => sum + (s.gradeCount || 0), 0);
    }

    // ---- Dispenses de matières OBLIGATOIRES (motif obligatoire) — logique pure, comme ci-dessus --------

    /** État initial du formulaire de dispenses à partir des matières obligatoires renvoyées par le serveur. */
    function exemptionStateFrom(mandatorySubjects) {
        return Object.fromEntries((mandatorySubjects || []).map((s) => [
            s.subjectId,
            { checked: !!s.isExempt, reason: s.reason ?? '' }
        ]));
    }

    /** Les dispenses à envoyer : uniquement les matières cochées, motif nettoyé. Liste vide si aucune case n'est cochée. */
    function exemptionsPayload(state) {
        return Object.entries(state || {})
            .filter(([, entry]) => entry && entry.checked)
            .map(([subjectId, entry]) => ({ subjectId, reason: (entry.reason ?? '').trim() }));
    }

    /** Noms des matières cochées dont le motif est vide : le serveur refuserait l'enregistrement (422). */
    function missingReasons(mandatorySubjects, state) {
        return (mandatorySubjects || [])
            .filter((s) => state && state[s.subjectId] && state[s.subjectId].checked
                && !(state[s.subjectId].reason ?? '').trim())
            .map((s) => s.name);
    }

    /** Notes de l'année que ces dispenses masqueraient : somme de `gradeCount` des matières cochées. */
    function hiddenExemptionGrades(mandatorySubjects, state) {
        return (mandatorySubjects || [])
            .filter((s) => state && state[s.subjectId] && state[s.subjectId].checked)
            .reduce((sum, s) => sum + (s.gradeCount || 0), 0);
    }

    window.subjectOptions = {
        groupsForLevel, choose, clearGroup, unchosenGroupLabels, hiddenGradeCount,
        exemptionStateFrom, exemptionsPayload, missingReasons, hiddenExemptionGrades
    };
})();
