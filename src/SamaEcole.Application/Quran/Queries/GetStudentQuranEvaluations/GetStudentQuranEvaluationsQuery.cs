using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetStudentQuranEvaluations;

/// <summary>GET /quran/evaluations?studentId= — historique complet des évaluations orales d'un élève.</summary>
public record GetStudentQuranEvaluationsQuery(Guid StudentId) : IRequest<IReadOnlyList<QuranEvaluationDto>>;
