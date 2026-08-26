using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Commands.AssignExamCenter;

public class AssignExamCenterCommandHandler(
    IApplicationDbContext dbContext,
    IExamCandidateNumberGenerator candidateNumberGenerator)
    : MediatR.IRequestHandler<AssignExamCenterCommand, ExamDossierResult>
{
    public async Task<ExamDossierResult> Handle(AssignExamCenterCommand request, CancellationToken cancellationToken) =>
        // Tout le flux — lecture, génération du numéro, écriture — DANS une seule transaction : si
        // SaveChangesAsync échoue plus loin (conflit xmin, doublon d'un numéro saisi à la main), le
        // compteur de la session est rembobiné avec elle, sans trou (AGENTS.md règle #3).
        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var dossier = await dbContext.ExamDossiers
                .FirstOrDefaultAsync(d => d.Id == request.Id, ct)
                ?? throw new KeyNotFoundException($"Dossier d'examen {request.Id} introuvable.");

            dbContext.SetOriginalConcurrencyToken(dossier, request.RowVersion);

            if (!string.IsNullOrWhiteSpace(request.ExamCenterName))
            {
                dossier.ExamCenterName = request.ExamCenterName.Trim();
            }
            else if (dossier.ExamCenterName is null)
            {
                var session = await dbContext.ExamSessions.AsNoTracking()
                    .Where(s => s.Id == dossier.ExamSessionId)
                    .Select(s => s.CenterName)
                    .FirstOrDefaultAsync(ct);

                dossier.ExamCenterName = session;
            }

            dossier.CandidateNumber = string.IsNullOrWhiteSpace(request.CandidateNumber)
                ? await candidateNumberGenerator.GenerateNextAsync(dossier.ExamSessionId, ct)
                : request.CandidateNumber.Trim();

            // Numéro déjà pris par un autre dossier de la session (saisie manuelle en collision) :
            // SaveChangesAsync le traduit en DuplicateRecordException -> 409 (AGENTS.md règle #5).
            await dbContext.SaveChangesAsync(ct);

            var rowVersion = await dbContext.ExamDossiers.AsNoTracking()
                .Where(d => d.Id == dossier.Id)
                .Select(d => EF.Property<uint>(d, "xmin"))
                .FirstAsync(ct);

            return new ExamDossierResult(
                dossier.Id,
                dossier.ExamSessionId,
                dossier.StudentId,
                dossier.ClassroomId,
                dossier.CandidateNumber,
                dossier.ExamCenterName,
                dossier.BirthCertificatePresent,
                dossier.CivilStatusConforming,
                dossier.Status.ToString(),
                rowVersion);
        }, cancellationToken);
}
