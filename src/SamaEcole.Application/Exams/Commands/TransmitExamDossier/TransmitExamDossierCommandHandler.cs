using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Commands.TransmitExamDossier;

public class TransmitExamDossierCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : MediatR.IRequestHandler<TransmitExamDossierCommand, ExamDossierResult>
{
    public async Task<ExamDossierResult> Handle(TransmitExamDossierCommand request, CancellationToken cancellationToken)
    {
        var dossier = await dbContext.ExamDossiers
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dossier d'examen {request.Id} introuvable.");

        if (dossier.Status == ExamDossierStatus.Incomplet)
        {
            throw new BusinessRuleException(
                "Ce dossier est encore incomplet : consultez GET /exams/dossiers/audit pour le détail "
                + "des pièces manquantes avant de le transmettre.");
        }

        // Complet -> Transmis. Déjà Transmis/Valide : idempotent, on ne réécrit pas TransmittedOn.
        if (dossier.Status == ExamDossierStatus.Complet)
        {
            dossier.Status = ExamDossierStatus.Transmis;
            dossier.TransmittedOn = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var rowVersion = await dbContext.ExamDossiers.AsNoTracking()
            .Where(d => d.Id == dossier.Id)
            .Select(d => EF.Property<uint>(d, "xmin"))
            .FirstAsync(cancellationToken);

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
    }
}
