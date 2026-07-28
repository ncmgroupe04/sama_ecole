using MediatR;

namespace SamaEcole.Application.Reports.Queries.GetAttendanceExport;

/// <summary>
/// GET /api/v1/reports/attendance/export?startDate=&amp;endDate=&amp;classId=&amp;format= — ticket JGK-R03.
/// Mêmes filtres et validation que le rapport R02, plus un format (pdf|csv). Le SchoolId vient du JWT
/// (règle #10) et le classId éventuel est validé comme appartenant au tenant (via l'agrégateur partagé).
/// </summary>
public record GetAttendanceExportQuery : IRequest<AttendanceExportResult>
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public Guid? ClassId { get; init; }

    /// <summary>« pdf » ou « csv » (insensible à la casse). Validé — un format inconnu est refusé en 422.</summary>
    public required string Format { get; init; }
}

/// <summary>Fichier prêt à télécharger : contenu, nom généré dynamiquement, et type MIME adéquat.</summary>
public record AttendanceExportResult(byte[] Content, string FileName, string ContentType);
