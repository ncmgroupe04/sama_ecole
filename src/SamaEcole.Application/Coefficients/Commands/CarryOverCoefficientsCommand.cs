using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Coefficients.Commands;

/// <summary>
/// POST /api/v1/coefficients/carry-over — « Reprendre l'année précédente » (Évolution N°4, arbitrage A6).
/// Une nouvelle année démarre SANS surcharge ; cette action, explicite, recopie celles de
/// <see cref="FromSchoolYearId"/> vers l'année ACTIVE, sans jamais écraser une surcharge déjà posée.
/// Une ligne dont la matière ou la classe a été archivée depuis n'est pas recopiée.
/// </summary>
public record CarryOverCoefficientsCommand(Guid FromSchoolYearId) : IRequest<CarryOverResult>, IAuditableRequest;

public record CarryOverResult(int Copied, int Skipped);

public class CarryOverCoefficientsCommandValidator : AbstractValidator<CarryOverCoefficientsCommand>
{
    public CarryOverCoefficientsCommandValidator() => RuleFor(x => x.FromSchoolYearId).NotEmpty();
}

public class CarryOverCoefficientsCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CarryOverCoefficientsCommand, CarryOverResult>
{
    public async Task<CarryOverResult> Handle(CarryOverCoefficientsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var activeYearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);

        if (request.FromSchoolYearId == activeYearId)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.FromSchoolYearId), "L'année source est déjà l'année active.")
            ]);
        }

        if (!await dbContext.SchoolYears.AnyAsync(y => y.Id == request.FromSchoolYearId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.FromSchoolYearId), "L'année source n'existe pas dans votre établissement.")
            ]);
        }

        var source = await dbContext.SubjectCoefficientOverrides.AsNoTracking()
            .Where(o => o.SchoolYearId == request.FromSchoolYearId)
            .ToListAsync(cancellationToken);

        var alreadyThere = (await dbContext.SubjectCoefficientOverrides.AsNoTracking()
                .Where(o => o.SchoolYearId == activeYearId)
                .Select(o => new { o.SubjectId, o.ClassroomId, o.Series })
                .ToListAsync(cancellationToken))
            .Select(o => (o.SubjectId, o.ClassroomId, o.Series))
            .ToHashSet();

        // Le filtre global écarte les matières et classes archivées : on ne recopie pas de surcharge orpheline.
        var liveSubjects = (await dbContext.Subjects.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken)).ToHashSet();
        var liveClassrooms = (await dbContext.Classrooms.AsNoTracking().Select(c => c.Id).ToListAsync(cancellationToken)).ToHashSet();

        var copied = 0;
        foreach (var row in source)
        {
            var stillValid = liveSubjects.Contains(row.SubjectId)
                             && (row.ClassroomId is null || liveClassrooms.Contains(row.ClassroomId.Value));

            if (!stillValid || alreadyThere.Contains((row.SubjectId, row.ClassroomId, row.Series)))
            {
                continue;
            }

            dbContext.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
            {
                SchoolId = schoolId,
                SchoolYearId = activeYearId,
                SubjectId = row.SubjectId,
                ClassroomId = row.ClassroomId,
                Series = row.Series,
                Coefficient = row.Coefficient
            });
            copied++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CarryOverResult(copied, source.Count - copied);
    }
}
