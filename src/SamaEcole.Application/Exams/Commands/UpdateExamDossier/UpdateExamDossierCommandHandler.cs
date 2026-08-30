using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Commands.UpdateExamDossier;

public class UpdateExamDossierCommandHandler(IApplicationDbContext dbContext)
    : MediatR.IRequestHandler<UpdateExamDossierCommand, ExamDossierResult>
{
    public async Task<ExamDossierResult> Handle(UpdateExamDossierCommand request, CancellationToken cancellationToken)
    {
        var dossier = await dbContext.ExamDossiers
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dossier d'examen {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(dossier, request.RowVersion);

        dossier.ExamCenterName = string.IsNullOrWhiteSpace(request.ExamCenterName) ? null : request.ExamCenterName.Trim();
        dossier.BirthCertificateNumber = string.IsNullOrWhiteSpace(request.BirthCertificateNumber) ? null : request.BirthCertificateNumber.Trim();
        dossier.BirthCertificatePresent = request.BirthCertificatePresent;
        dossier.CivilStatusConforming = request.CivilStatusConforming;
        dossier.CivilStatusNotes = string.IsNullOrWhiteSpace(request.CivilStatusNotes) ? null : request.CivilStatusNotes.Trim();

        // null = champ absent de la requête : on préserve la valeur en base plutôt que de la
        // ramener au défaut NonFourni. Le couple booléen ci-dessus reste la source du statut
        // Incomplet/Complet ; ce champ ne fait que le COMPLÉTER pour l'IEF (cas EnRegularisation).
        if (request.CivilRegistryDocumentStatus is { } civilRegistryStatus)
        {
            dossier.CivilRegistryDocumentStatus = civilRegistryStatus;
        }

        // Le passage Incomplet <-> Complet se recalcule à chaque correction (Volume 1 §22.2). Une fois
        // Transmis ou Valide, une correction ultérieure (ex. faute de frappe repérée après coup) ne
        // fait pas régresser le dossier dans le cycle : seuls TransmitExamDossier et RecordExamResult
        // font avancer ces deux états-là.
        if (dossier.Status is ExamDossierStatus.Incomplet or ExamDossierStatus.Complet)
        {
            dossier.Status = dossier.BirthCertificatePresent && dossier.CivilStatusConforming == true
                ? ExamDossierStatus.Complet
                : ExamDossierStatus.Incomplet;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

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
