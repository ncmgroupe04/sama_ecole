using MediatR;

namespace SamaEcole.Application.Reports.Queries.GetAttendanceReport;

/// <summary>
/// GET /api/v1/reports/attendance?startDate=&amp;endDate=&amp;classId=&amp;page=&amp;pageSize= — ticket JGK-R02.
/// Rapport d'assiduité détaillé par classe et par élève, sur une période bornée.
///
/// Réservé au Directeur, au Secrétariat et au Super Admin (voir ReportsController). Comme partout, le
/// SchoolId n'est jamais un paramètre : il vient du JWT (AGENTS.md règle #10). Le <see cref="ClassId"/>
/// fourni est validé côté serveur comme appartenant à l'école courante — un identifiant d'une autre
/// école est refusé, jamais silencieusement ignoré.
/// </summary>
public record GetAttendanceReportQuery : IRequest<AttendanceReportDto>
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    /// <summary>Filtre optionnel sur une classe. Null = toutes les classes de l'école.</summary>
    public Guid? ClassId { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Statistiques d'assiduité d'un élève sur la période. Le taux ne porte que sur ses appels réels.</summary>
public record StudentAttendanceReportRow(
    Guid StudentId,
    string Matricule,
    string FullName,
    Guid ClassroomId,
    string ClassroomName,
    int TotalCalls,
    int Present,
    int Late,
    int JustifiedAbsences,
    int UnjustifiedAbsences,
    int TotalLateMinutes,
    decimal AttendanceRate,

    // Évolution N°5 : la JOURNÉE de l'élève, classée d'après les séances appelées (DayAttendanceClassifier).
    // Aucun nouveau statut : c'est un calcul, et le taux ci-dessus n'en dépend pas. Les défauts gardent
    // compatible toute construction existante de la ligne.
    /// <summary>Jours où au moins une séance a été appelée pour cet élève : le dénominateur des trois nombres suivants.</summary>
    int DaysRecorded = 0,
    /// <summary>Jours où il a été absent à TOUTES les séances appelées.</summary>
    int FullAbsenceDays = 0,
    /// <summary>Jours où il a été absent à au moins une séance mais présent (ou en retard) à une autre.</summary>
    int PartialAbsenceDays = 0,
    /// <summary>Jours sans aucune absence, avec au moins un retard.</summary>
    int LateOnlyDays = 0);

/// <summary>Une matière dans la vue « par matière » du rapport d'assiduité.</summary>
public record SubjectAttendanceReportRow(
    Guid SubjectId,
    string SubjectName,
    int Sessions,
    int Lines,
    int Presents,
    int Lates,
    int JustifiedAbsences,
    int UnjustifiedAbsences,
    decimal AttendanceRate);

public record AttendanceReportDto(
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? ClassId,
    /// <summary>Taux moyen (0..1) = (Présents + Retards) / total des appels de la période. Null si aucun appel.</summary>
    decimal? AverageAttendanceRate,
    IReadOnlyList<StudentAttendanceReportRow> Students,
    int TotalCount,
    int Page,
    int PageSize);
