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
        // Rôle non borné (ownTeacherId null) : Directeur/Secrétariat peuvent toujours corriger,
        // entrée récente ou non — c'est précisément leur rôle passé le délai.
        if (ownTeacherId is null) return;

        var isAuthor = entry.TeacherId == ownTeacherId;
        var withinWindow = today <= entry.SessionDate.AddDays(Days);

        if (isAuthor && withinWindow) return;

        throw new ForbiddenException(isAuthor
            ? $"Le délai de {Days} jours pour corriger vous-même cette entrée est dépassé : seuls le Directeur et le Secrétariat peuvent la corriger désormais."
            : "Vous ne pouvez modifier que vos propres entrées de journal.");
    }
}
