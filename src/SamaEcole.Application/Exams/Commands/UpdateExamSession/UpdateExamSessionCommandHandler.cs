using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Commands.CreateExamSession;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Commands.UpdateExamSession;

public class UpdateExamSessionCommandHandler(IApplicationDbContext dbContext)
    : MediatR.IRequestHandler<UpdateExamSessionCommand, ExamSessionResult>
{
    public async Task<ExamSessionResult> Handle(UpdateExamSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await dbContext.ExamSessions
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Session d'examen {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(session, request.RowVersion);

        session.CenterName = string.IsNullOrWhiteSpace(request.CenterName) ? null : request.CenterName.Trim();
        session.Status = request.Status;

        await dbContext.SaveChangesAsync(cancellationToken);

        var dossierCount = await dbContext.ExamDossiers.AsNoTracking()
            .CountAsync(d => d.ExamSessionId == session.Id, cancellationToken);

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
            dossierCount,
            rowVersion);
    }
}
