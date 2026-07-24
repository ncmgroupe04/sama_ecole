using MediatR;

namespace SamaEcole.Application.Absences.Queries.GetLateArrivals;

public record LateArrivalDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentFullName { get; init; } = null!;
    public string StudentMatricule { get; init; } = null!;
    public DateTime Date { get; init; }
    public int Minutes { get; init; }
    public string Reason { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
}

public record GetLateArrivalsQuery : IRequest<List<LateArrivalDto>>;
