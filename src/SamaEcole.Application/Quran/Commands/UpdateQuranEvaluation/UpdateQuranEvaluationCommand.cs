using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;

/// <summary>
/// PUT /quran/evaluations/{id} — corrige une évaluation déjà saisie. Ne touche jamais l'Élève
/// (identité de la ligne, immuable — décision #8 de la spec). RowVersion : verrou optimiste (règle #5).
/// </summary>
public record UpdateQuranEvaluationCommand(
    Guid Id,
    DateOnly EvaluationDate,
    int MemoryMistakes,
    int TajwidMistakes,
    int Hesitations,
    decimal FinalScore,
    uint RowVersion)
    : IRequest<QuranEvaluationDto>, IAuditableRequest;
