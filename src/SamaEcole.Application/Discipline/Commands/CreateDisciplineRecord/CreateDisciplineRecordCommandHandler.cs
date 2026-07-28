using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Discipline.Commands.CreateDisciplineRecord;

public class CreateDisciplineRecordCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider)
    : IRequestHandler<CreateDisciplineRecordCommand, Guid>
{
    public async Task<Guid> Handle(CreateDisciplineRecordCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var studentExists = await _context.Students.FindAsync(new object[] { request.StudentId }, cancellationToken);
        if (studentExists == null)
            throw new NotFoundException(nameof(Student), request.StudentId.ToString());

        var record = new DisciplineRecord
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            StudentId = request.StudentId,
            Date = request.Date,
            Type = request.Type,
            Reason = request.Reason
        };

        _context.DisciplineRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
