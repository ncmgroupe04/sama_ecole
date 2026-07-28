using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;

public record CreateAbsenceJustificationCommand : IRequest<Guid>
{
    public Guid StudentId { get; init; }
    public DateTime Date { get; init; }
    public string Reason { get; init; } = null!;
    public DateTime? AuthorizedReturnDate { get; init; }
}
