using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Coefficients.Commands;

/// <summary>
/// PUT /api/v1/coefficients — pose ou corrige une surcharge de coefficient (Évolution N°4). Portée :
/// <see cref="Series"/> OU <see cref="ClassroomId"/>, exactement l'une des deux. L'année est l'année ACTIVE,
/// résolue serveur. <see cref="RowVersion"/> est le jeton xmin lu avec la ligne (CoefficientGridRow) : absent
/// pour une création, obligatoire pour une correction (verrou optimiste, règle #5).
///
/// <see cref="IAuditableRequest"/> : changer un coefficient recalcule des moyennes et des bulletins — une
/// écriture sensible, historisée (arbitrage A7).
/// </summary>
public record UpsertCoefficientOverrideCommand : IRequest<CoefficientOverrideResult>, IAuditableRequest
{
    public required Guid SubjectId { get; init; }
    public required decimal Coefficient { get; init; }
    public string? Series { get; init; }
    public Guid? ClassroomId { get; init; }
    public uint? RowVersion { get; init; }
}

public record CoefficientOverrideResult(
    Guid Id, Guid SubjectId, string? Series, Guid? ClassroomId, decimal Coefficient, uint RowVersion);

public class UpsertCoefficientOverrideCommandValidator : AbstractValidator<UpsertCoefficientOverrideCommand>
{
    public UpsertCoefficientOverrideCommandValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();

        // Mêmes bornes que Subject.Coefficient (CreateSubjectCommandValidator).
        RuleFor(x => x.Coefficient)
            .GreaterThan(0).WithMessage("Le coefficient doit être supérieur à zéro.")
            .LessThanOrEqualTo(CoefficientRules.MaxCoefficient)
                .WithMessage($"Le coefficient annoncé semble irréaliste (maximum {CoefficientRules.MaxCoefficient:0}).");

        // Exactement UNE portée : la série OU la classe (la base le tient aussi, CHECK).
        RuleFor(x => x)
            .Must(x => (LyceeSeries.Normalize(x.Series) is not null) ^ (x.ClassroomId is not null))
            .OverridePropertyName("Scope")
            .WithMessage("Précisez soit une série, soit une classe — jamais les deux.");

        RuleFor(x => x.Series)
            .Must(s => LyceeSeries.IsValid(LyceeSeries.Normalize(s)))
            .When(x => LyceeSeries.Normalize(x.Series) is not null)
            .WithMessage("Série inconnue : choisissez parmi L1, L2, S1, S2 ou TECH.");
    }
}

public class UpsertCoefficientOverrideCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<UpsertCoefficientOverrideCommand, CoefficientOverrideResult>
{
    public async Task<CoefficientOverrideResult> Handle(
        UpsertCoefficientOverrideCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);
        var series = LyceeSeries.Normalize(request.Series);

        var subjectLevel = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == request.SubjectId)
            .Select(s => s.Level)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);

        if (CoefficientRules.IsPrimaryLevel(subjectLevel))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), CoefficientRules.PrimaryMessage)
            ]);
        }

        if (request.ClassroomId is { } classroomId)
        {
            var cycle = await CoefficientRules.ClassroomCycleAsync(dbContext, classroomId, cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
                ]);

            if (cycle.UsesSimplifiedGrading())
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.ClassroomId), CoefficientRules.PrimaryMessage)
                ]);
            }
        }

        var existing = await dbContext.SubjectCoefficientOverrides
            .Where(o => o.SchoolYearId == yearId && o.SubjectId == request.SubjectId
                        && (series != null ? o.Series == series : o.ClassroomId == request.ClassroomId))
            .FirstOrDefaultAsync(cancellationToken);

        SubjectCoefficientOverride row;
        if (existing is null)
        {
            // Une création ne porte pas de jeton : en fournir un signifie que la ligne a été retirée
            // (« Rétablir » par quelqu'un d'autre) depuis que l'écran l'a lue — un conflit, pas une création.
            if (request.RowVersion is not null)
            {
                throw new ConcurrencyConflictException("subject_coefficient_overrides", "ligne supprimée entre-temps");
            }

            row = new SubjectCoefficientOverride
            {
                SchoolId = schoolId,
                SchoolYearId = yearId,
                SubjectId = request.SubjectId,
                Series = series,
                ClassroomId = series is null ? request.ClassroomId : null,
                Coefficient = request.Coefficient
            };
            dbContext.SubjectCoefficientOverrides.Add(row);
        }
        else
        {
            // Une correction SANS jeton reviendrait à écraser sans avoir lu : refusée en 409, jamais silencieuse.
            if (request.RowVersion is null)
            {
                throw new ConcurrencyConflictException("subject_coefficient_overrides", existing.Id);
            }

            dbContext.SetOriginalConcurrencyToken(existing, request.RowVersion.Value);
            existing.Coefficient = request.Coefficient;
            row = existing;
        }

        // Une violation de l'index unique (deux écritures simultanées de la même clé) est traduite en 409
        // par ApplicationDbContext.SaveChangesAsync — jamais un doublon silencieux (règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.SubjectCoefficientOverrides.AsNoTracking()
            .Where(o => o.Id == row.Id)
            .Select(o => EF.Property<uint>(o, "xmin"))
            .FirstAsync(cancellationToken);

        return new CoefficientOverrideResult(
            row.Id, row.SubjectId, row.Series, row.ClassroomId, row.Coefficient, rowVersion);
    }
}
