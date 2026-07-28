using FluentValidation;

namespace SamaEcole.Application.Reports.Queries.GetAttendanceExport;

public class GetAttendanceExportQueryValidator : AbstractValidator<GetAttendanceExportQuery>
{
    /// <summary>Même borne de fenêtre que le rapport paginé R02.</summary>
    public const int MaxRangeDays = 400;

    public static readonly string[] AllowedFormats = ["pdf", "csv"];

    public GetAttendanceExportQueryValidator()
    {
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("La date de fin doit être postérieure ou égale à la date de début.");

        RuleFor(x => x)
            .Must(x => x.EndDate.DayNumber - x.StartDate.DayNumber <= MaxRangeDays)
            .WithMessage($"La période ne peut pas dépasser {MaxRangeDays} jours.")
            .When(x => x.EndDate >= x.StartDate);

        RuleFor(x => x.Format)
            .NotEmpty()
            .Must(f => f is not null && AllowedFormats.Contains(f.Trim().ToLowerInvariant()))
            .WithMessage("Le format doit être « pdf » ou « csv ».");
    }
}
