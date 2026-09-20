using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.UpdateQuranProgress;

/// <summary>
/// PUT /quran/progress/{id} — corrige une observation déjà saisie. Ne touche JAMAIS Juz/Hizb/
/// Sourate/Élève (identité de la ligne, immuable — décision #8 de la spec, même philosophie que
/// UpdateGradeCommand qui ne touche que Value).
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation (AGENTS.md règle #5) :
/// un jeton périmé fait échouer SaveChangesAsync en 409.
/// </summary>
public record UpdateQuranProgressCommand(
    Guid Id,
    QuranMemorizationStatus Status,
    DateOnly? EvaluationDate,
    string? Notes,
    uint RowVersion)
    : IRequest<QuranProgressDto>, IAuditableRequest;
