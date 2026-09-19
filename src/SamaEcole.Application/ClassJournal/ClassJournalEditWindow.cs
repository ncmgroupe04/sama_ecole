using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.ClassJournal;

/// <summary>
/// Règle des 15 jours (ticket JGK-P04) : une entrée de journal reste librement modifiable par son
/// auteur pendant 15 jours après <see cref="ClassJournalEntry.SessionDate"/> ; passé ce délai,
/// seuls Directeur et Secrétariat peuvent la corriger (même esprit que la règle #4 Finance —
/// AGENTS.md : on ne réécrit pas silencieusement un historique, la correction se fait par les
/// rôles élevés et reste tracée). Partagée par Update et Delete pour ne pas dupliquer le calcul.
/// </summary>
public static class ClassJournalEditWindow
{
    public const int Days = 15;

    /// <summary>
    /// Lève un <see cref="ForbiddenException"/> — 403, refus de rôle/propriété, jamais 409 (réservé
    /// à l'absence de créneau planifié à la création, voir ClassJournalScopeAuthorizer) — si
    /// l'appelant n'a pas le droit de corriger cette entrée.
    /// </summary>
    /// <param name="ownTeacherId">
    /// Fiche enseignant de l'appelant, ou <c>null</c> pour un rôle non borné (Directeur/Secrétariat) —
    /// voir <see cref="ClassJournalScopeAuthorizer.GetOwnTeacherIdOrNullAsync"/>.
    /// </param>
    public static void EnsureCanCorrect(ClassJournalEntry entry, Guid? ownTeacherId, DateOnly today)
    {
        if (CanCorrect(entry, ownTeacherId, today)) return;

        var isAuthor = ownTeacherId is not null && entry.TeacherId == ownTeacherId;
        throw new ForbiddenException(isAuthor
            ? $"Le délai de {Days} jours pour corriger vous-même cette entrée est dépassé : seuls le Directeur et le Secrétariat peuvent la corriger désormais."
            : "Vous ne pouvez modifier que vos propres entrées de journal.");
    }

    /// <summary>
    /// Version pure, sans exception : utilisée par GetClassJournalQueryHandler pour poser le champ
    /// <c>CanEdit</c> de chaque ligne de la liste — l'écran masque Modifier/Supprimer sur cette base
    /// plutôt que de recalculer la règle côté client (qui n'a ni l'heure serveur ni, pour
    /// l'Enseignant, un moyen fiable de connaître sa propre fiche autrement).
    /// </summary>
    public static bool CanCorrect(ClassJournalEntry entry, Guid? ownTeacherId, DateOnly today) =>
        CanCorrect(entry.TeacherId, entry.SessionDate, ownTeacherId, today);

    /// <summary>
    /// Même règle, sans entité chargée : utilisée sur une projection de liste (GetClassJournalQueryHandler),
    /// où construire une <see cref="ClassJournalEntry"/> juste pour ce calcul serait un détour inutile.
    /// </summary>
    public static bool CanCorrect(Guid teacherId, DateOnly sessionDate, Guid? ownTeacherId, DateOnly today)
    {
        // Rôle non borné (ownTeacherId null) : Directeur/Secrétariat peuvent toujours corriger,
        // entrée récente ou non — c'est précisément leur rôle passé le délai.
        if (ownTeacherId is null) return true;

        return teacherId == ownTeacherId && today <= sessionDate.AddDays(Days);
    }
}
