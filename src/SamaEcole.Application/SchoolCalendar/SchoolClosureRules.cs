using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Entities;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.SchoolCalendar;

/// <summary>Règles communes à la création et à la modification d'une fermeture.</summary>
internal static class SchoolClosureRules
{
    public static IRuleBuilderOptions<T, string> ValidLabel<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Le libellé est obligatoire.")
            .MaximumLength(120).WithMessage("Le libellé ne peut pas dépasser 120 caractères.")
            .NoHtml();

    public static IRuleBuilderOptions<T, string?> ValidLabelAr<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(120).WithMessage("Le libellé arabe ne peut pas dépasser 120 caractères.")
            .NoHtml();

    /// <summary>
    /// Charge l'année visée et vérifie que la période tient DANS ses bornes : une fermeture hors de
    /// l'année n'aurait aucun effet sur un cahier et ne serait qu'une faute de saisie. Année d'une
    /// autre école ou inexistante = 404 (Global Query Filter + RLS).
    /// </summary>
    public static async Task<SchoolYear> EnsureWithinYearAsync(
        IApplicationDbContext dbContext, Guid schoolYearId, DateOnly start, DateOnly end, string field,
        CancellationToken cancellationToken)
    {
        var year = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == schoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {schoolYearId} introuvable.");

        if (start < year.StartDate || end > year.EndDate)
        {
            throw new ValidationException([
                new ValidationFailure(field,
                    $"La fermeture doit tenir dans l'année scolaire « {year.Label} » "
                    + $"({year.StartDate:dd/MM/yyyy} – {year.EndDate:dd/MM/yyyy}).")
            ]);
        }

        return year;
    }
}
