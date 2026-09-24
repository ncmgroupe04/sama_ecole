using SamaEcole.Application.Quran;
using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranProgress;

/// <summary>
/// GET /quran/progress/classroom/{classroomId} — un élève, sa liste d'observations. Pas de forme
/// fixe (contrairement à StudentGradeRowDto/Devoir1/2/Composition) : le nombre d'observations par
/// élève est libre (décision Phase 1 §3.3).
/// </summary>
public record GetClassQuranProgressQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranProgressRowDto>>;

public record ClassQuranProgressRowDto(
    Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranProgressDto> Entries);
