using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;

public class CreateAbsenceJustificationCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider)
    : IRequestHandler<CreateAbsenceJustificationCommand, Guid>
{
    public async Task<Guid> Handle(CreateAbsenceJustificationCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var studentExists = await _context.Students.FindAsync(new object[] { request.StudentId }, cancellationToken);
        if (studentExists == null)
            throw new NotFoundException(nameof(Student), request.StudentId.ToString());

        var justification = new AbsenceJustification
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            StudentId = request.StudentId,
            Date = request.Date,
            Reason = request.Reason,
            AuthorizedReturnDate = request.AuthorizedReturnDate
        };

        _context.AbsenceJustifications.Add(justification);
        await _context.SaveChangesAsync(cancellationToken);

        return justification.Id;
    }
}
