using MediatR;

namespace SamaEcole.Application.Exams.Commands.DispatchExamConvocations;

/// <summary>
/// POST /api/v1/exams/sessions/{id}/dispatch-convocations — dispatch en lot des convocations par
/// SMS. Réutilise le canal de notification existant (<see cref="Common.Interfaces.ISmsDispatcher"/>,
/// Feature.SmsNotifications) : aucun nouveau canal, aucune nouvelle table.
/// </summary>
public record DispatchExamConvocationsCommand(Guid ExamSessionId) : IRequest<DispatchExamConvocationsResult>;

public record DispatchExamConvocationsResult(int QueuedCount, int SkippedCount, string? FirstSkipReason);
