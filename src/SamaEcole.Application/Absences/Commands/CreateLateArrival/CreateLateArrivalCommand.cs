using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateLateArrival;

public record CreateLateArrivalCommand : IRequest<Guid>
{
    public Guid StudentId { get; init; }
    public DateTime Date { get; init; }
    public int Minutes { get; init; }
    public string Reason { get; init; } = null!;
    public string? Observations { get; init; }
}
