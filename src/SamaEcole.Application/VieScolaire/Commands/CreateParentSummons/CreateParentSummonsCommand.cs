using MediatR;

namespace SamaEcole.Application.VieScolaire.Commands.CreateParentSummons;

public record CreateParentSummonsCommand : IRequest<Guid>
{
    public Guid StudentId { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public string Reason { get; init; } = null!;
}
