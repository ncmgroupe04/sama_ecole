using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Exams.Commands.RecordExamResult;

public class RecordExamResultCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : MediatR.IRequestHandler<RecordExamResultCommand, ExamResultDto>
{
    public async Task<ExamResultDto> Handle(RecordExamResultCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var dossier = await dbContext.ExamDossiers
            .FirstOrDefaultAsync(d => d.Id == request.ExamDossierId, cancellationToken)
            ?? throw new KeyNotFoundException($"Dossier d'examen {request.ExamDossierId} introuvable.");

        // Résultat saisi après transmission (première saisie) ou en correction d'un résultat déjà
        // saisi (Valide) — jamais avant, un dossier encore Incomplet/Complet n'a pas été transmis.
        if (dossier.Status is ExamDossierStatus.Incomplet or ExamDossierStatus.Complet)
        {
            throw new BusinessRuleException(
                "Ce dossier n'a pas encore été transmis à l'IEF/l'IA : transmettez-le avant de saisir un résultat.");
        }

        var examType = await dbContext.ExamSessions.AsNoTracking()
            .Where(s => s.Id == dossier.ExamSessionId)
            .Select(s => s.ExamType)
            .FirstAsync(cancellationToken);

        if (examType == ExamType.CFEE && request.Mention is not null)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Mention), "Le CFEE n'attribue pas de mention.")
            ]);
        }

        var result = await dbContext.ExamResults
            .FirstOrDefaultAsync(r => r.ExamDossierId == request.ExamDossierId, cancellationToken);

        if (result is null)
        {
            result = new ExamResult { SchoolId = schoolId, ExamDossierId = request.ExamDossierId };
            dbContext.ExamResults.Add(result);
        }

        result.IsAdmitted = request.IsAdmitted;
        result.Mention = request.Mention;
        result.AverageScore = request.AverageScore;
        result.DeliberatedOn = request.DeliberatedOn;

        dossier.Status = ExamDossierStatus.Valide;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ExamResultDto(
            result.Id,
            result.ExamDossierId,
            result.IsAdmitted,
            result.Mention,
            result.AverageScore,
            result.DeliberatedOn);
    }
}
