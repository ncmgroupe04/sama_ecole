using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Institutional.Queries;

/// <summary>GET /api/v1/institutional/age-norms — tranche d'âge de chaque niveau : modèle national et réglage de l'école.</summary>
public record GetAgeNormsQuery : IRequest<IReadOnlyList<AgeNormRowDto>>;

/// <param name="IsCustom">Vrai si l'école a réglé ce niveau ; « Revenir au modèle » archive alors son réglage.</param>
public record AgeNormRowDto(
    string GradeLevel, CycleType Cycle, int TemplateMinAge, int TemplateMaxAge, int MinAge, int MaxAge, bool IsCustom);

public class GetAgeNormsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetAgeNormsQuery, IReadOnlyList<AgeNormRowDto>>
{
    public async Task<IReadOnlyList<AgeNormRowDto>> Handle(GetAgeNormsQuery request, CancellationToken cancellationToken)
    {
        var custom = await dbContext.GradeAgeNorms.AsNoTracking()
            .ToDictionaryAsync(n => n.GradeLevel, cancellationToken);

        return AgeNormTemplates.All
            .Select(t => custom.TryGetValue(t.GradeLevel, out var c)
                ? new AgeNormRowDto(t.GradeLevel, t.Cycle, t.MinAge, t.MaxAge, c.MinAge, c.MaxAge, true)
                : new AgeNormRowDto(t.GradeLevel, t.Cycle, t.MinAge, t.MaxAge, t.MinAge, t.MaxAge, false))
            .ToList();
    }
}

/// <summary>
/// GET /api/v1/institutional/age-check?classroomId=&amp;birthDate= — contrôle de la tranche d'âge à l'INSCRIPTION
/// (Évolution N°7). Un AVERTISSEMENT, jamais un blocage : un élève en avance ou en retard s'inscrit, mais le
/// secrétariat le voit et le rapport IEF le compte.
/// </summary>
public record CheckEnrollmentAgeQuery(Guid ClassroomId, DateOnly BirthDate) : IRequest<AgeCheckDto>;

/// <param name="GradeLevel">Niveau de la classe ; null si son nom ne le dit pas (aucun contrôle possible).</param>
/// <param name="Message">Phrase prête à afficher ; null pour un âge normal ou un niveau inconnu.</param>
public record AgeCheckDto(string? GradeLevel, int? Age, int? MinAge, int? MaxAge, AgeNormStatus Status, DateOnly ReferenceDate, string? Message);

public class CheckEnrollmentAgeQueryValidator : AbstractValidator<CheckEnrollmentAgeQuery>
{
    public CheckEnrollmentAgeQueryValidator() => RuleFor(x => x.ClassroomId).NotEmpty();
}

public class CheckEnrollmentAgeQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<CheckEnrollmentAgeQuery, AgeCheckDto>
{
    public async Task<AgeCheckDto> Handle(CheckEnrollmentAgeQuery request, CancellationToken cancellationToken)
    {
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == request.ClassroomId)
            .Select(c => new { c.Name, c.Cycle })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");

        // Année ACTIVE (celle de l'inscription), à défaut l'année civile en cours.
        var yearStart = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (DateOnly?)y.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var referenceDate = AgeRules.ReferenceDate(yearStart);
        var grade = AgeRules.GradeOf(classroom.Name, classroom.Cycle);
        var norms = await AgeRules.ResolveAsync(dbContext, cancellationToken);
        var norm = grade is not null && norms.TryGetValue(grade, out var n) ? n : null;
        var age = AgeRules.AgeAt(request.BirthDate, referenceDate);
        var status = AgeRules.Classify(age, norm);

        var message = status switch
        {
            AgeNormStatus.Early => $"Âge hors norme : {age} ans au {referenceDate:dd/MM/yyyy}, pour une tranche de {norm!.MinAge} à {norm.MaxAge} ans en {grade} (élève en avance).",
            AgeNormStatus.Late => $"Âge hors norme : {age} ans au {referenceDate:dd/MM/yyyy}, pour une tranche de {norm!.MinAge} à {norm.MaxAge} ans en {grade} (élève en retard).",
            _ => null
        };

        return new AgeCheckDto(grade, age, norm?.MinAge, norm?.MaxAge, status, referenceDate, message);
    }
}
