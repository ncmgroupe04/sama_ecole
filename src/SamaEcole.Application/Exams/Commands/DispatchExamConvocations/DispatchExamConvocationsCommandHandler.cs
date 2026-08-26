using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Exams.Commands.DispatchExamConvocations;

public class DispatchExamConvocationsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISmsDispatcher smsDispatcher,
    ILogger<DispatchExamConvocationsCommandHandler> logger)
    : IRequestHandler<DispatchExamConvocationsCommand, DispatchExamConvocationsResult>
{
    public async Task<DispatchExamConvocationsResult> Handle(
        DispatchExamConvocationsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var session = await dbContext.ExamSessions.AsNoTracking()
            .Where(s => s.Id == request.ExamSessionId)
            .Select(s => new { s.ExamType })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Session d'examen {request.ExamSessionId} introuvable.");

        var rows = await dbContext.ExamDossiers.AsNoTracking()
            .Where(d => d.ExamSessionId == request.ExamSessionId)
            .Select(d => new
            {
                d.Id,
                d.StudentId,
                d.CandidateNumber,
                d.ExamCenterName,
                StudentFullName = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.FullName).FirstOrDefault() ?? "",
                GuardianPhone = dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.GuardianPhone).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        // Tout ou rien : un dispatch partiel laisserait croire à un parent que son enfant n'a pas de
        // convocation alors qu'elle n'a simplement pas encore été attribuée (Volume 1 §22.5).
        if (rows.Any(r => string.IsNullOrWhiteSpace(r.CandidateNumber) || string.IsNullOrWhiteSpace(r.ExamCenterName)))
        {
            throw new BusinessRuleException(
                "Un ou plusieurs dossiers de cette session n'ont pas encore de centre ou de numéro de "
                + "table : attribuez-les tous avant de dispatcher les convocations.");
        }

        var queuedCount = 0;
        var skippedCount = 0;
        string? firstSkipReason = null;

        foreach (var row in rows)
        {
            var body =
                $"Convocation {session.ExamType} : {row.StudentFullName}, centre {row.ExamCenterName}, "
                + $"table n°{row.CandidateNumber}. Se présenter muni(e) d'une pièce d'identité.";

            var outcome = await smsDispatcher.DispatchAsync(
                new SmsDispatchRequest(schoolId, row.GuardianPhone, body, SmsTrigger.ExamConvocation, row.StudentId),
                cancellationToken);

            if (outcome.IsQueued)
            {
                queuedCount++;
            }
            else
            {
                skippedCount++;
                firstSkipReason ??= outcome.Reason;
            }
        }

        logger.LogInformation(
            "Convocations de la session {ExamSessionId} dispatchées pour l'établissement {SchoolId} : "
            + "{Queued} mise(s) en file, {Skipped} écartée(s).",
            request.ExamSessionId, schoolId, queuedCount, skippedCount);

        return new DispatchExamConvocationsResult(queuedCount, skippedCount, firstSkipReason);
    }
}
