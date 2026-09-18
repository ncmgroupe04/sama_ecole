namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Suppression d'une année scolaire ET de tout ce qui s'y rattache — EN MODE TEST UNIQUEMENT.
///
/// Implémenté dans SamaEcole.Persistence pour les mêmes raisons que
/// <see cref="IResetSchoolDataService"/> : la purge doit atteindre les lignes en suppression logique
/// (donc contourner le Global Query Filter), et l'ordre des clés étrangères est une connaissance de
/// persistance. Elle passe par la fonction PostgreSQL SECURITY DEFINER <c>delete_school_year</c>
/// (migration AddSchoolYearDeletion) : le rôle applicatif n'a aucun droit de DELETE sur ces tables.
///
/// EN MODE RÉEL, ce service n'est jamais appelé — et la fonction refuserait de toute façon. Une année
/// qui a servi porte des inscriptions, des reçus remis aux parents et des écritures comptables :
/// <c>DeleteSchoolYearCommandHandler</c> exige alors qu'elle soit VIDE, et se contente d'une
/// suppression logique (AGENTS.md règle #6).
/// </summary>
public interface ISchoolYearPurgeService
{
    /// <summary>
    /// Efface, dans UNE transaction, l'année <paramref name="schoolYearId"/> de l'établissement
    /// <paramref name="schoolId"/> et les données qui la portent. Les ÉLÈVES survivent : ils
    /// appartiennent à l'établissement, pas à un exercice — seules leurs inscriptions à cette année
    /// disparaissent.
    /// </summary>
    Task<SchoolDataResetSummary> PurgeAsync(
        Guid schoolId, Guid schoolYearId, CancellationToken cancellationToken);
}
