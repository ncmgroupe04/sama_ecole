using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateEarlyDeparture;

public record CreateEarlyDepartureCommand : IRequest<Guid>
{
    public Guid StudentId { get; init; }
    public DateTime Date { get; init; }
    public TimeOnly DepartureTime { get; init; }
    public string Reason { get; init; } = null!;
    public string? PickedUpBy { get; init; }
}
