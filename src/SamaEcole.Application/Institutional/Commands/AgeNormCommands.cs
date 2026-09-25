using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Institutional.Commands;

/// <summary>
/// PUT /api/v1/institutional/age-norms/{gradeLevel} — l'école règle la tranche d'âge d'un niveau (Directeur seul).
/// Le niveau doit appartenir au modèle national (nomenclature fermée) : une tranche sur « 6e bis » ne serait
/// jamais lue.
/// </summary>
public record UpsertAgeNormCommand(string GradeLevel, int MinAge, int MaxAge) : IRequest<Unit>, IAuditableRequest;

public class UpsertAgeNormCommandValidator : AbstractValidator<UpsertAgeNormCommand>
{
    public UpsertAgeNormCommandValidator()
    {
        RuleFor(x => x.GradeLevel)
            .Must(g => AgeNormTemplates.For(g) is not null)
            .WithMessage("Niveau inconnu : choisissez un niveau de la liste (CI, CP… Terminale).");
        RuleFor(x => x.MinAge).InclusiveBetween(0, AgeRules.MaxPlausibleAge);
        RuleFor(x => x.MaxAge).InclusiveBetween(0, AgeRules.MaxPlausibleAge)
            .GreaterThanOrEqualTo(x => x.MinAge).WithMessage("L'âge maximal doit être au moins égal à l'âge minimal.");
    }
}

public class UpsertAgeNormCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<UpsertAgeNormCommand, Unit>
{
    public async Task<Unit> Handle(UpsertAgeNormCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var row = await dbContext.GradeAgeNorms.FirstOrDefaultAsync(n => n.GradeLevel == request.GradeLevel, cancellationToken);
        if (row is null)
        {
            row = new GradeAgeNorm { SchoolId = schoolId, GradeLevel = request.GradeLevel };
            dbContext.GradeAgeNorms.Add(row);
        }

        row.MinAge = request.MinAge;
        row.MaxAge = request.MaxAge;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>DELETE /api/v1/institutional/age-norms/{gradeLevel} — « Revenir au modèle » : archive le réglage de l'école.</summary>
public record ResetAgeNormCommand(string GradeLevel) : IRequest<Unit>, IAuditableRequest;

public class ResetAgeNormCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<ResetAgeNormCommand, Unit>
{
    public async Task<Unit> Handle(ResetAgeNormCommand request, CancellationToken cancellationToken)
    {
        var row = await dbContext.GradeAgeNorms.FirstOrDefaultAsync(n => n.GradeLevel == request.GradeLevel, cancellationToken);
        if (row is not null)
        {
            row.SoftDelete(currentUser.UserId?.ToString() ?? "system");
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
