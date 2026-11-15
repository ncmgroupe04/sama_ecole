using Microsoft.EntityFrameworkCore;
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

        var student = await _context.Students.FindAsync(new object[] { request.StudentId }, cancellationToken);
        if (student == null)
            throw new NotFoundException(nameof(Student), request.StudentId.ToString());

        var end = request.EndDate ?? request.StartDate;

        // Deux billets actifs ne se chevauchent pas : un jour n'est justifié qu'une fois (l'annulé est soft-deleted,
        // donc hors du filtre global).
        var overlaps = await _context.AbsenceJustifications.AnyAsync(a =>
            a.StudentId == request.StudentId && a.StartDate <= end && a.EndDate >= request.StartDate, cancellationToken);
        if (overlaps)
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("StartDate", "Cet élève a déjà un billet d'absence qui couvre une partie de ces jours.")]);
        }

        var justification = new AbsenceJustification
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            StudentId = request.StudentId,
            StartDate = request.StartDate,
            EndDate = end,
            Reason = request.Reason.Trim(),
            AuthorizedReturnDate = request.AuthorizedReturnDate
        };

        _context.AbsenceJustifications.Add(justification);
        await new AbsenceJustificationRegister(_context).ApplyAsync(justification, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return justification.Id;
    }
}
