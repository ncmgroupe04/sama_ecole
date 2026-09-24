using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;

public class CreateQuranEvaluationCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateQuranEvaluationCommand, QuranEvaluationDto>
{
    public async Task<QuranEvaluationDto> Handle(CreateQuranEvaluationCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "L'élève indiqué n'existe pas dans votre établissement.")
            ]);
        }

        var evaluation = new QuranEvaluation
        {
            SchoolId = schoolId,
            StudentId = request.StudentId,
            EvaluationDate = request.EvaluationDate,
            MemoryMistakes = request.MemoryMistakes,
            TajwidMistakes = request.TajwidMistakes,
            Hesitations = request.Hesitations,
            FinalScore = request.FinalScore
        };

        dbContext.QuranEvaluations.Add(evaluation);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.QuranEvaluations.AsNoTracking()
            .Where(e => e.Id == evaluation.Id)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .FirstAsync(cancellationToken);

        return new QuranEvaluationDto(
            evaluation.Id, evaluation.StudentId, evaluation.EvaluationDate, evaluation.MemoryMistakes,
            evaluation.TajwidMistakes, evaluation.Hesitations, evaluation.FinalScore, rowVersion);
    }
}
