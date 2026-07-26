using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateEarlyDeparture;

public class CreateEarlyDepartureCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider)
    : IRequestHandler<CreateEarlyDepartureCommand, Guid>
{
    public async Task<Guid> Handle(CreateEarlyDepartureCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var studentExists = await _context.Students.FindAsync(new object[] { request.StudentId }, cancellationToken);
        if (studentExists == null)
            throw new NotFoundException(nameof(Student), request.StudentId.ToString());

        var earlyDeparture = new EarlyDeparture
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            StudentId = request.StudentId,
            Date = request.Date,
            DepartureTime = request.DepartureTime,
            Reason = request.Reason,
            PickedUpBy = request.PickedUpBy
        };

        _context.EarlyDepartures.Add(earlyDeparture);
        await _context.SaveChangesAsync(cancellationToken);

        return earlyDeparture.Id;
    }
}
