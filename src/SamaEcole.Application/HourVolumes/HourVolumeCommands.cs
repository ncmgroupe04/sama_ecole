using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Institutional;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.HourVolumes;

internal static class HourVolumeRules
{
    public const decimal MaxWeeklyHours = 40m;

    public static string? NormalizeSeries(string? series) => LyceeSeries.Normalize(series);

    /// <summary>
    /// Un niveau de la nomenclature (CI … Terminale) et, s'il y en a une, une série du catalogue — sur un niveau de
    /// lycée seulement : une série en Sixième ne serait jamais lue par le contrôle.
    /// </summary>
    public static void EnsureValidScope(string? grade, string? series)
    {
        var norm = AgeNormTemplates.For(grade);
        if (norm is null)
        {
            throw new ValidationException([new ValidationFailure("GradeLevel", "Niveau inconnu : choisissez un niveau de la liste (CI, CP… Terminale).")]);
        }

        if (series is null) return;

        if (!LyceeSeries.IsValid(series))
        {
            throw new ValidationException([new ValidationFailure("Series", LyceeSeries.UnknownMessage)]);
        }

        if (norm.Cycle != Domain.Enums.CycleType.Lycee)
        {
            throw new ValidationException([new ValidationFailure("Series", "Une série ne se règle qu'au lycée (Seconde, Première, Terminale).")]);
        }
    }
}

/// <summary>
/// PUT /api/v1/hour-volumes/norms — l'école règle le volume hebdomadaire d'une matière pour un niveau (et une série),
/// Directeur seul. Crée le réglage ou le met à jour (<see cref="RowVersion"/> obligatoire alors : 409 s'il a changé).
/// </summary>
public record UpsertWeeklyHourNormCommand(string GradeLevel, string? Series, Guid SubjectId, decimal WeeklyHours, uint? RowVersion)
    : IRequest<Unit>, IAuditableRequest;

public class UpsertWeeklyHourNormCommandValidator : AbstractValidator<UpsertWeeklyHourNormCommand>
{
    public UpsertWeeklyHourNormCommandValidator()
    {
        RuleFor(x => x.GradeLevel).NotEmpty();
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.WeeklyHours).InclusiveBetween(0, HourVolumeRules.MaxWeeklyHours)
            .WithMessage($"Le volume hebdomadaire doit être compris entre 0 et {HourVolumeRules.MaxWeeklyHours} heures.");
        RuleFor(x => x.WeeklyHours).Must(h => h * 4 == Math.Truncate(h * 4))
            .WithMessage("Le volume hebdomadaire se saisit au quart d'heure (ex. 2, 2,5 ou 1,75).");
    }
}

public class UpsertWeeklyHourNormCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<UpsertWeeklyHourNormCommand, Unit>
{
    public async Task<Unit> Handle(UpsertWeeklyHourNormCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var series = HourVolumeRules.NormalizeSeries(request.Series);
        HourVolumeRules.EnsureValidScope(request.GradeLevel, series);

        if (!await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure("SubjectId", "La matière indiquée n'existe pas dans votre établissement.")]);
        }

        var row = await dbContext.WeeklyHourNorms.FirstOrDefaultAsync(
            n => n.GradeLevel == request.GradeLevel && n.Series == series && n.SubjectId == request.SubjectId, cancellationToken);

        if (row is null)
        {
            dbContext.WeeklyHourNorms.Add(new WeeklyHourNorm
            {
                SchoolId = schoolId, GradeLevel = request.GradeLevel, Series = series, SubjectId = request.SubjectId,
                WeeklyHours = request.WeeklyHours
            });
        }
        else
        {
            if (request.RowVersion is not { } rowVersion)
            {
                throw new ValidationException([new ValidationFailure("RowVersion", "Ce volume est déjà réglé : rechargez la liste avant de le modifier.")]);
            }

            dbContext.SetOriginalConcurrencyToken(row, rowVersion);
            row.WeeklyHours = request.WeeklyHours;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>DELETE /api/v1/hour-volumes/norms/{id}?rowVersion= — « Revenir à la référence » : archive le réglage (Directeur).</summary>
public record ResetWeeklyHourNormCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;

public class ResetWeeklyHourNormCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<ResetWeeklyHourNormCommand, Unit>
{
    public async Task<Unit> Handle(ResetWeeklyHourNormCommand request, CancellationToken cancellationToken)
    {
        var row = await dbContext.WeeklyHourNorms.FirstOrDefaultAsync(n => n.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Volume horaire {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(row, request.RowVersion);
        row.SoftDelete(currentUser.UserId?.ToString() ?? "system");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
