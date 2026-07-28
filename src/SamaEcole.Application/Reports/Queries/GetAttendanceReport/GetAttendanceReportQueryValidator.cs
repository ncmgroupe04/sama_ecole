using FluentValidation;

namespace SamaEcole.Application.Reports.Queries.GetAttendanceReport;

public class GetAttendanceReportQueryValidator : AbstractValidator<GetAttendanceReportQuery>
{
    public const int MaxPageSize = 100;

    /// <summary>
    /// Fenêtre maximale d'un rapport : une année scolaire large. Au-delà, la requête n'a plus de sens
    /// métier et ouvre la porte à un balayage coûteux — la borne protège la base autant que l'utilisateur.
    /// </summary>
    public const int MaxRangeDays = 400;

    public GetAttendanceReportQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(MaxPageSize);

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");

        RuleFor(x => x)
            .Must(x => x.EndDate.DayNumber - x.StartDate.DayNumber <= MaxRangeDays)
            .WithMessage($"La période ne peut pas dépasser {MaxRangeDays} jours.")
            .When(x => x.EndDate >= x.StartDate);
    }
}
