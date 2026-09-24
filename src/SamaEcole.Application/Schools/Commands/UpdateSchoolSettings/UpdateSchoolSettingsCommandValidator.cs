using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;

public class UpdateSchoolSettingsCommandValidator : AbstractValidator<UpdateSchoolSettingsCommand>
{
    public UpdateSchoolSettingsCommandValidator()
    {
        RuleFor(c => c.GradingScale)
            .Must(scale => SchoolSettingsDefaults.AllowedGradingScales.Contains(ParseScale(scale)))
            .WithMessage("Le barème doit valoir « 10 » ou « 20 ».");

        RuleFor(c => c.DateFormat)
            .Must(format => SchoolSettingsDefaults.AllowedDateFormats.Contains(format))
            .WithMessage("Format de date inconnu : « dd/MM/yyyy » ou « dd MMMM yyyy ».");

        // Une déconnexion à 0 minute déconnecterait l'utilisateur en boucle ; au-delà d'une journée,
        // le réglage ne protège plus personne (docs/Volume_7_Security.md §5).
        RuleFor(c => c.AutoLogoutMinutes)
            .InclusiveBetween(1, 480)
            .WithMessage("La déconnexion automatique doit être comprise entre 1 et 480 minutes.");

        // Le nombre de mensualités multiplie chaque scolarité à l'inscription (JGK-E01) : 0 rendrait
        // toute scolarité gratuite, au-delà de 12 il déborderait l'année civile.
        RuleFor(c => c.TuitionMonthsPerYear)
            .InclusiveBetween(SchoolSettingsDefaults.MinTuitionMonths, SchoolSettingsDefaults.MaxTuitionMonths)
            .WithMessage(
                $"Le nombre de mensualités par an doit être compris entre {SchoolSettingsDefaults.MinTuitionMonths} et {SchoolSettingsDefaults.MaxTuitionMonths}.");

        // Le gabarit est validé AVANT d'être stocké : un format sans {SEQ} donnerait le même matricule
        // à tous les élèves, et la panne n'apparaîtrait qu'à la 2e inscription — en pleine rentrée.
        RuleFor(c => c.StudentMatriculeFormat)
            .Must(format => MatriculeFormat.Validate(format) is null)
            .WithMessage(c => MatriculeFormat.Validate(c.StudentMatriculeFormat) ?? string.Empty);

        RuleFor(c => c.TeacherMatriculeFormat)
            .Must(format => MatriculeFormat.Validate(format) is null)
            .WithMessage(c => MatriculeFormat.Validate(c.TeacherMatriculeFormat) ?? string.Empty);

        RuleFor(c => c.DebtorReminderThresholdDays)
            .InclusiveBetween(
                SchoolSettingsDefaults.MinDebtorReminderThresholdDays,
                SchoolSettingsDefaults.MaxDebtorReminderThresholdDays)
            .WithMessage(
                $"Le seuil de retard doit être compris entre {SchoolSettingsDefaults.MinDebtorReminderThresholdDays} et {SchoolSettingsDefaults.MaxDebtorReminderThresholdDays} jours.");

        RuleFor(c => c.GradeEditWindowDays)
            .InclusiveBetween(
                SchoolSettingsDefaults.MinGradeEditWindowDays,
                SchoolSettingsDefaults.MaxGradeEditWindowDays)
            .WithMessage(
                $"Le délai de correction des notes doit être compris entre {SchoolSettingsDefaults.MinGradeEditWindowDays} et {SchoolSettingsDefaults.MaxGradeEditWindowDays} jours.");
    }

    private static int ParseScale(string? scale) =>
        int.TryParse(scale, out var value) ? value : -1;
}
