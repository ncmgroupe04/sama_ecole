using SamaEcole.Application.Quran;
using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranProgress;

/// <summary>GET /quran/progress?studentId= — historique complet de suivi d'un élève.</summary>
public record GetStudentQuranProgressQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranProgressDto>>;
