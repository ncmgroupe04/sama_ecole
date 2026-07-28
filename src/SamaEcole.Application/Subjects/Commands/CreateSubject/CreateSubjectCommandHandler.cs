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

        var subject = new Subject
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            Level = request.Level.Trim(),
            Coefficient = request.Coefficient
        };

        dbContext.Subjects.Add(subject);

        // Deux matières de même nom au même niveau dans la même école violent l'index unique :
        // SaveChangesAsync traduit la violation en ConcurrencyConflictException → 409, jamais un
        // écrasement silencieux ni un 500 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return new SubjectResult(subject.Id, subject.Name, subject.Level, subject.Coefficient);
    }
}
