namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Purge des données OPÉRATIONNELLES d'un établissement (« remise à neuf » après une phase d'essai).
///
/// Implémenté dans SamaEcole.Persistence, et non dans un Handler, pour deux raisons :
///   1. la purge doit atteindre AUSSI les lignes en suppression logique (IsDeleted = true), donc
///      contourner le Global Query Filter — une manipulation d'EF Core qui n'a pas sa place dans
///      SamaEcole.Application (même motif que GetGlobalAuditLogsAsync sur IApplicationDbContext) ;
///   2. l'ordre de suppression suit les clés étrangères du schéma, une connaissance de persistance.
///
/// EXCEPTION ASSUMÉE à la règle #6 d'AGENTS.md (« aucune suppression physique ») : c'est ici la
/// finalité même de l'opération — le Directeur demande explicitement l'effacement définitif de ses
/// données de test. L'exception est bornée à ce service, déclenchée par le seul Directeur de
/// l'école concernée, sur SON tenant, et journalisée (ResetSchoolDataCommand est IAuditableRequest).
/// </summary>
public interface IResetSchoolDataService
{
    /// <summary>
    /// Efface, dans UNE transaction, les données opérationnelles de <paramref name="schoolId"/>.
    /// La configuration de l'établissement (réglages, années scolaires, classes, matières, barème de
    /// frais, mentions, enseignants, utilisateurs) et le journal d'audit sont CONSERVÉS.
    /// </summary>
    Task<SchoolDataResetSummary> ResetAsync(Guid schoolId, CancellationToken cancellationToken);
}

/// <summary>Compte rendu d'une purge : ce qui a réellement été effacé, table par table.</summary>
/// <param name="TotalRowsDeleted">Total des lignes effacées, tous domaines confondus.</param>
/// <param name="Entries">Détail par table, dans l'ordre de suppression.</param>
public sealed record SchoolDataResetSummary(
    int TotalRowsDeleted,
    IReadOnlyList<SchoolDataResetEntry> Entries);

/// <summary>Une ligne du compte rendu : le nom fonctionnel du domaine purgé et le nombre de lignes.</summary>
public sealed record SchoolDataResetEntry(string Label, int RowsDeleted);
