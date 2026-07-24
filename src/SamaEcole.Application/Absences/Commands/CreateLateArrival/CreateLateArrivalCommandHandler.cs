using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateLateArrival;

public class CreateLateArrivalCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider)
    : IRequestHandler<CreateLateArrivalCommand, Guid>
{
    public async Task<Guid> Handle(CreateLateArrivalCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var studentExists = await _context.Students.FindAsync(new object[] { request.StudentId }, cancellationToken);
        if (studentExists == null)
            throw new NotFoundException(nameof(Student), request.StudentId.ToString());

        var lateArrival = new LateArrival
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            StudentId = request.StudentId,
            Date = request.Date,
            Minutes = request.Minutes,
            Reason = request.Reason
        };

        _context.LateArrivals.Add(lateArrival);
        await _context.SaveChangesAsync(cancellationToken);

        return lateArrival.Id;
    }
}
