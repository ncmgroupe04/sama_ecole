using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;

public class UpdateQuranEvaluationCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateQuranEvaluationCommand, QuranEvaluationDto>
{
    public async Task<QuranEvaluationDto> Handle(UpdateQuranEvaluationCommand request, CancellationToken cancellationToken)
    {
        var evaluation = await dbContext.QuranEvaluations
            .FirstOrDefaultAsync(e => e.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Évaluation coranique {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(evaluation, request.RowVersion);

        evaluation.EvaluationDate = request.EvaluationDate;
        evaluation.MemoryMistakes = request.MemoryMistakes;
        evaluation.TajwidMistakes = request.TajwidMistakes;
        evaluation.Hesitations = request.Hesitations;
        evaluation.FinalScore = request.FinalScore;

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
