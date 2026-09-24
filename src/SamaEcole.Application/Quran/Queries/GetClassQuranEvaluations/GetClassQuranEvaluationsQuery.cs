using MediatR;

namespace SamaEcole.Application.Quran.Queries.GetClassQuranEvaluations;

public record GetClassQuranEvaluationsQuery(Guid ClassroomId) : IRequest<IReadOnlyList<ClassQuranEvaluationRowDto>>;

public record ClassQuranEvaluationRowDto(
    Guid StudentId, string Matricule, string FullName, IReadOnlyList<QuranEvaluationDto> Entries);
