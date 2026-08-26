using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Commands.CreateExamSession;

public class CreateExamSessionCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : MediatR.IRequestHandler<CreateExamSessionCommand, ExamSessionResult>
{
    public async Task<ExamSessionResult> Handle(CreateExamSessionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var session = new ExamSession
        {
            SchoolId = schoolId,
            SchoolYearId = request.SchoolYearId,
            ExamType = request.ExamType,
            Series = string.IsNullOrWhiteSpace(request.Series) ? null : request.Series.Trim(),
            CenterName = string.IsNullOrWhiteSpace(request.CenterName) ? null : request.CenterName.Trim()
        };

        dbContext.ExamSessions.Add(session);

        // Doublon (même année, type, série) refusé par l'index unique posé en migration (COALESCE sur
        // Series) : SaveChangesAsync le traduit en DuplicateRecordException -> 409 (AGENTS.md règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.ExamSessions.AsNoTracking()
            .Where(s => s.Id == session.Id)
            .Select(s => EF.Property<uint>(s, "xmin"))
            .FirstAsync(cancellationToken);

        return new ExamSessionResult(
            session.Id,
            session.SchoolYearId,
            session.ExamType,
            session.Series,
            session.CenterName,
            session.Status.ToString(),
            DossierCount: 0,
            rowVersion);
    }
}
