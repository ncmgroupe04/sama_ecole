using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Discipline.Queries.GetDisciplineRecords;

public record DisciplineRecordDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentFullName { get; init; } = null!;
    public string StudentMatricule { get; init; } = null!;
    public DateTime Date { get; init; }
    public DisciplineType Type { get; init; }
    public string Reason { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
}

public record GetDisciplineRecordsQuery : IRequest<List<DisciplineRecordDto>>;
