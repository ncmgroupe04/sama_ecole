using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards.Commands.UpsertReportCardRemark;

public class UpsertReportCardRemarkCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<UpsertReportCardRemarkCommand, ReportCardRemarkDto>
{
    public async Task<ReportCardRemarkDto> Handle(UpsertReportCardRemarkCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Même contrôle d'appartenance qu'ailleurs (Grade, StudentAttendance) : le Global Query Filter
        // rend un élève/trimestre d'une autre école introuvable ici — une erreur de saisie exploitable
        // sur le bon champ, plutôt qu'une DbUpdateException remontée en 500.
        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "L'élève indiqué n'existe pas dans votre établissement.")
            ]);
        }

        if (!await dbContext.Terms.AnyAsync(t => t.Id == request.TermId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.TermId), "Le trimestre indiqué n'existe pas dans votre établissement.")
            ]);
        }

        var remark = await dbContext.ReportCardRemarks
            .FirstOrDefaultAsync(r => r.StudentId == request.StudentId && r.TermId == request.TermId, cancellationToken);

        if (remark is null)
        {
            remark = new ReportCardRemark { SchoolId = schoolId, StudentId = request.StudentId, TermId = request.TermId };
            dbContext.ReportCardRemarks.Add(remark);
        }

        remark.DisciplinaryMention = request.DisciplinaryMention;
        remark.Observations = string.IsNullOrWhiteSpace(request.Observations) ? null : request.Observations.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ReportCardRemarkDto(remark.StudentId, remark.TermId, remark.DisciplinaryMention, remark.Observations);
    }
}
