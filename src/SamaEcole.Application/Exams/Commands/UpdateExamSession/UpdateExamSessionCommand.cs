using SamaEcole.Application.Exams.Commands.CreateExamSession;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Exams.Commands.UpdateExamSession;

/// <summary>PUT /api/v1/exams/sessions/{id} — corrige le centre par défaut ou le statut d'une session.</summary>
public record UpdateExamSessionCommand : IRequest<ExamSessionResult>
{
    public Guid Id { get; init; }
    public string? CenterName { get; init; }
    public ExamSessionStatus Status { get; init; }
    public required uint RowVersion { get; init; }
}
