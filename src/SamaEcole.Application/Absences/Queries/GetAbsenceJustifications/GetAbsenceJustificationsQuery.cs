using MediatR;

namespace SamaEcole.Application.Absences.Queries.GetAbsenceJustifications;

public record AbsenceJustificationDto
{
    public Guid Id { get; init; }
    public Guid StudentId { get; init; }
    public string StudentFullName { get; init; } = null!;
    public string StudentMatricule { get; init; } = null!;
    public DateTime Date { get; init; }
    public string Reason { get; init; } = null!;
    public DateTime? AuthorizedReturnDate { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record GetAbsenceJustificationsQuery : IRequest<List<AbsenceJustificationDto>>;
