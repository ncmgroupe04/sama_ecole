using FluentValidation;
using MediatR;
using SamaEcole.Application.Reports.Queries.GetAttendanceReport;

namespace SamaEcole.Application.Reports.Queries.GetAttendanceBySubjectReport;

/// <summary>
/// GET /api/v1/reports/attendance/by-subject?startDate=&amp;endDate=&amp;classId= — vue « par matière » du rapport
/// d'assiduité (Évolution N°5) : séances appelées et répartition des statuts, matière par matière.
///
/// Mêmes filtres, même garde de classe et mêmes permissions que le rapport par élève (Directeur, Secrétariat,
/// Super Admin) ; le calcul vit dans <see cref="AttendanceReportAggregator"/>. Le SchoolId vient du JWT.
/// </summary>
public record GetAttendanceBySubjectReportQuery : IRequest<IReadOnlyList<SubjectAttendanceReportRow>>
{
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }

    /// <summary>Filtre optionnel sur une classe. Null = toutes les classes de l'école.</summary>
    public Guid? ClassId { get; init; }
}

public class GetAttendanceBySubjectReportQueryValidator : AbstractValidator<GetAttendanceBySubjectReportQuery>
{
    public GetAttendanceBySubjectReportQueryValidator()
    {
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");

        RuleFor(x => x)
            .Must(x => x.EndDate.DayNumber - x.StartDate.DayNumber <= GetAttendanceReportQueryValidator.MaxRangeDays)
            .WithMessage($"La période ne peut pas dépasser {GetAttendanceReportQueryValidator.MaxRangeDays} jours.")
            .When(x => x.EndDate >= x.StartDate);
    }
}

public class GetAttendanceBySubjectReportQueryHandler(AttendanceReportAggregator aggregator)
    : IRequestHandler<GetAttendanceBySubjectReportQuery, IReadOnlyList<SubjectAttendanceReportRow>>
{
    public Task<IReadOnlyList<SubjectAttendanceReportRow>> Handle(
        GetAttendanceBySubjectReportQuery request, CancellationToken cancellationToken)
        => aggregator.ComputeBySubjectAsync(request.StartDate, request.EndDate, request.ClassId, cancellationToken);
}
