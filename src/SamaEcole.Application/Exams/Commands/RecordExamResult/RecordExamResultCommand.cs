using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Exams.Commands.RecordExamResult;

/// <summary>PUT /api/v1/exams/dossiers/{id}/result — saisit le résultat et la mention à la délibération.</summary>
public record RecordExamResultCommand : IRequest<ExamResultDto>
{
    public Guid ExamDossierId { get; init; }
    public required bool IsAdmitted { get; init; }

    /// <summary>Nul si non admis, ou si la session est CFEE (pas de mention).</summary>
    public ExamMention? Mention { get; init; }

    public decimal? AverageScore { get; init; }
    public required DateOnly DeliberatedOn { get; init; }
}

public record ExamResultDto(
    Guid Id,
    Guid ExamDossierId,
    bool IsAdmitted,
    ExamMention? Mention,
    decimal? AverageScore,
    DateOnly DeliberatedOn);
