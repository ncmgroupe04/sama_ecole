using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Quran.Commands.CreateQuranProgress;

/// <summary>
/// POST /quran/progress — module Coran/Franco-Arabe (spec Phase 2 §3.1). Réservé au Directeur et à
/// l'Enseignant, comme CreateGradeCommand — aucune vérification d'affectation enseignant/matière
/// (décision #1 de la spec, il n'existe aucun sous-rôle "Enseignant Coran").
///
/// Aucune contrainte d'unicité : plusieurs observations pour le même (StudentId, SurahNumber) sont
/// légitimes au fil du temps (décision Phase 1 §3.3).
///
/// IAuditableRequest (JGK-H01) : comme la saisie de notes (décision #6 de la spec).
/// </summary>
public record CreateQuranProgressCommand(
    Guid StudentId,
    int JuzNumber,
    int HizbNumber,
    int SurahNumber,
    QuranMemorizationStatus Status,
    DateOnly? EvaluationDate,
    string? Notes)
    : IRequest<QuranProgressDto>, IAuditableRequest;
