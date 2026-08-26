using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Exams.Commands.CreateExamSession;

/// <summary>POST /api/v1/exams/sessions — crée une campagne d'examen. Le SchoolId vient du JWT (AGENTS.md règle #10).</summary>
public record CreateExamSessionCommand : IRequest<ExamSessionResult>
{
    public required Guid SchoolYearId { get; init; }
    public required ExamType ExamType { get; init; }

    /// <summary>Nul pour CFEE, qui n'a pas de série (Volume 1 §22.1).</summary>
    public string? Series { get; init; }

    public string? CenterName { get; init; }
}

public record ExamSessionResult(
    Guid Id,
    Guid SchoolYearId,
    ExamType ExamType,
    string? Series,
    string? CenterName,
    string Status,
    int DossierCount,
    uint RowVersion);
