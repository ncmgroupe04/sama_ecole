using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Subjects.Commands.CreateSubject;

public class CreateSubjectCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<CreateSubjectCommand, SubjectResult>
{
    public async Task<SubjectResult> Handle(CreateSubjectCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Une activité vit au niveau de son domaine, jamais à un autre (voir SubjectHierarchyGuard) : la
        // validité du rattachement et le niveau retenu se décident d'un seul appel.
        var level = await SubjectHierarchyGuard.ResolveLevelForParentAsync(
            dbContext, request.ParentSubjectId, subjectId: null, request.Level.Trim(), cancellationToken);

        var subject = new Subject
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            NameAr = Trimmed(request.NameAr),
            Level = level,
            Coefficient = request.Coefficient,
            ParentSubjectId = request.ParentSubjectId,
            MaxScore = request.MaxScore,
            DisplayOrder = request.DisplayOrder,

            // Les entêtes de colonnes qualifient la GRILLE, pas une ligne : une activité n'en porte
            // aucun, même si le client en envoie — sans quoi deux lignes du même tableau pourraient
            // prétendre en nommer les colonnes différemment.
            Column1Header = request.ParentSubjectId is null ? Trimmed(request.Column1Header) : null,
            Column2Header = request.ParentSubjectId is null ? Trimmed(request.Column2Header) : null
        };

        dbContext.Subjects.Add(subject);

        // Deux matières de même nom sous le même domaine au même niveau dans la même école violent
        // l'index unique : SaveChangesAsync traduit la violation en ConcurrencyConflictException → 409,
        // jamais un écrasement silencieux ni un 500 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SubjectResult(
            subject.Id, subject.Name, subject.Level, subject.Coefficient,
            subject.ParentSubjectId, subject.MaxScore, subject.DisplayOrder, subject.NameAr);
    }

    /// <summary>Entête vide ou blanche = « pas d'entête personnalisé » (null), jamais une chaîne vide stockée.</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
