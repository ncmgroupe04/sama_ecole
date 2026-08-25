using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace SamaEcole.Application.Teachers.Commands.UnassignTeacher;

public class UnassignTeacherCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<UnassignTeacherCommand>
{
    public async Task Handle(UnassignTeacherCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var assignment = await dbContext.TeacherAssignments
            .FirstOrDefaultAsync(a => a.Id == request.AssignmentId && a.TeacherId == request.TeacherId, cancellationToken)
            ?? throw new KeyNotFoundException($"Affectation introuvable.");

        dbContext.TeacherAssignments.Remove(assignment);
        
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
