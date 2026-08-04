namespace SamaEcole.Application.Teachers;

/// <summary>
/// Données prêtes à rendre pour l'export PDF des enseignants (GET /teachers/export/pdf). Pendant
/// exact de <c>StudentsExportModel</c> : le tenant et le périmètre sont déjà résolus par le Handler,
/// ce modèle n'est qu'un instantané et ne porte aucune requête.
/// </summary>
public record TeachersExportModel(
    string SchoolName,
    /// <summary>Libellé du filtre de statut appliqué, ou null pour « tous les statuts ».</summary>
    string? StatusLabel,
    IReadOnlyList<TeacherExportRow> Teachers);

public record TeacherExportRow(
    string Matricule,
    string FullName,
    DateOnly BirthDate,
    string? Phone,
    string Email,
    string StatusLabel,
    /// <summary>Matières enseignées, déjà agrégées en une seule chaîne par le Handler.</summary>
    string Subjects);
