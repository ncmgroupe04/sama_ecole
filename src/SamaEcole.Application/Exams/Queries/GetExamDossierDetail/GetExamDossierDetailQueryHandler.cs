using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Commands.RecordExamResult;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamDossierDetail;

public class GetExamDossierDetailQueryHandler(IApplicationDbContext dbContext, ExamDossierScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<GetExamDossierDetailQuery, ExamDossierDetail>
{
    public async Task<ExamDossierDetail> Handle(GetExamDossierDetailQuery request, CancellationToken cancellationToken)
    {
        var dossier = await dbContext.ExamDossiers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dossier d'examen {request.Id} introuvable.");

        // Portée JGK-J08 : une URL tapée directement sur un dossier hors des classes assignées de
        // l'Enseignant renvoie un refus explicite (403), jamais un 404 qui laisserait deviner l'état
        // du dossier par le seul code de statut.
        await scopeAuthorizer.EnsureCanReadClassroomAsync(dossier.ClassroomId, cancellationToken);

        var studentFullName = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == dossier.StudentId)
            .Select(s => s.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Élève supprimé";

        var classroomName = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == dossier.ClassroomId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Classe supprimée";

        var result = await dbContext.ExamResults.AsNoTracking()
            .Where(r => r.ExamDossierId == dossier.Id)
            .Select(r => new ExamResultDto(r.Id, r.ExamDossierId, r.IsAdmitted, r.Mention, r.AverageScore, r.DeliberatedOn))
            .FirstOrDefaultAsync(cancellationToken);

        // EF.Property<T> n'est valide QUE dans une requête LINQ traduite en SQL — appelé directement
        // sur l'objet déjà matérialisé, il lève toujours une InvalidOperationException. Ce bug était
        // présent avant JGK-J08 (GET /exams/dossiers/{id} renvoyait 500 sans exception) : le premier
        // test à exercer réellement ce Handler (ExamDossierScopeTests) l'a mis au jour.
        var rowVersion = await dbContext.ExamDossiers.AsNoTracking()
            .Where(d => d.Id == dossier.Id)
            .Select(d => EF.Property<uint>(d, "xmin"))
            .FirstAsync(cancellationToken);

        return new ExamDossierDetail(
            dossier.Id,
            dossier.ExamSessionId,
            dossier.StudentId,
            studentFullName,
            dossier.ClassroomId,
            classroomName,
            dossier.CandidateNumber,
            dossier.ExamCenterName,
            dossier.BirthCertificatePresent,
            dossier.CivilStatusConforming,
            dossier.Status.ToString(),
            rowVersion,
            dossier.BirthCertificateNumber,
            dossier.CivilStatusNotes,
            dossier.TransmittedOn,
            result,
            dossier.ExamCenterCode,
            dossier.TableNumber,
            dossier.CivilRegistryDocumentStatus.ToString());
    }
}
