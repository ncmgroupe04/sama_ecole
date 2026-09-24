using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;

/// <summary>
/// POST /quran/evaluations — module Coran/Franco-Arabe (spec Phase 2 §3.2). Réservé au Directeur et
/// à l'Enseignant, comme CreateGradeCommand. Aucune contrainte d'unicité, aucun plafond métier sur
/// FinalScore (décision #7 — aucun barème spécifié pour l'examen oral).
///
/// IAuditableRequest (JGK-H01), comme la saisie de notes.
/// </summary>
public record CreateQuranEvaluationCommand(
    Guid StudentId,
    DateOnly EvaluationDate,
    int MemoryMistakes,
    int TajwidMistakes,
    int Hesitations,
    decimal FinalScore)
    : IRequest<QuranEvaluationDto>, IAuditableRequest;
