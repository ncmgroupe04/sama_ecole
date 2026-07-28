using SamaEcole.Application.Reports.Queries.GetAttendanceReport;

namespace SamaEcole.Application.Reports;

/// <summary>
/// Données prêtes à rendre pour l'export d'assiduité (ticket JGK-R03), consommées à l'identique par le
/// CSV (<see cref="AttendanceReportCsv"/>) et le PDF (IAttendanceReportPdfGenerator). Le tenant et le
/// périmètre sont déjà résolus en amont — ce modèle ne porte aucune requête, il n'est qu'un instantané.
/// </summary>
public record AttendanceReportExportModel(
    string SchoolName,
    DateOnly StartDate,
    DateOnly EndDate,
    /// <summary>Nom de la classe filtrée, ou null pour « toutes les classes ».</summary>
    string? ClassName,
    decimal? AverageAttendanceRate,
    IReadOnlyList<StudentAttendanceReportRow> Students);
