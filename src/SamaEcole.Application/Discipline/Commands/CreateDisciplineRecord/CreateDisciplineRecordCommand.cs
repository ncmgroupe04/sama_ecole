using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Discipline.Commands.CreateDisciplineRecord;

public record CreateDisciplineRecordCommand : IRequest<Guid>
{
    public Guid StudentId { get; init; }
    public DateTime Date { get; init; }
    public DisciplineType Type { get; init; }
    public string Reason { get; init; } = null!;
}
