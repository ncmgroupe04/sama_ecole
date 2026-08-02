namespace SamaEcole.Application.Students;

/// <summary>
/// Données prêtes à rendre pour l'export PDF des élèves (GET /students/export/pdf). Le tenant et le
/// périmètre sont déjà résolus en amont par le Handler — ce modèle ne porte aucune requête, il n'est
/// qu'un instantané, comme <c>AttendanceReportExportModel</c> pour l'assiduité.
/// </summary>
public record StudentsExportModel(
    string SchoolName,
    /// <summary>Nom de la classe filtrée, ou null pour « toutes les classes ».</summary>
    string? ClassName,
    /// <summary>Année scolaire active, ou null si le filtre ActiveYearOnly n'est pas appliqué.</summary>
    string? SchoolYearLabel,
    IReadOnlyList<StudentExportRow> Students);

public record StudentExportRow(
    string Matricule,
    string FullName,
    DateOnly BirthDate,
    string Gender,
    string ClassroomName,
    string? GuardianName,
    string? GuardianPhone);
