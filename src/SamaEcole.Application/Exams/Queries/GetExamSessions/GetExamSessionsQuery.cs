using SamaEcole.Application.Exams.Commands.CreateExamSession;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamSessions;

/// <summary>GET /api/v1/exams/sessions — sessions d'examen de l'école, filtrables.</summary>
public record GetExamSessionsQuery : IRequest<IReadOnlyList<ExamSessionResult>>
{
    public Guid? SchoolYearId { get; init; }
    public ExamType? ExamType { get; init; }
}
