using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.VieScolaire.Commands.CreateParentSummons;

public class CreateParentSummonsCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider)
    : IRequestHandler<CreateParentSummonsCommand, Guid>
{
    public async Task<Guid> Handle(CreateParentSummonsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var studentExists = await _context.Students.FindAsync(new object[] { request.StudentId }, cancellationToken);
        if (studentExists == null)
            throw new NotFoundException(nameof(Student), request.StudentId.ToString());

        var summons = new ParentSummons
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            StudentId = request.StudentId,
            ScheduledAt = request.ScheduledAt,
            Reason = request.Reason
        };

        _context.ParentSummons.Add(summons);
        await _context.SaveChangesAsync(cancellationToken);

        return summons.Id;
    }
}
