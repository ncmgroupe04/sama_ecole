using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateAbsenceJustification;

/// <summary>Billet d'absence : justifie l'absence d'un élève de <see cref="StartDate"/> à <see cref="EndDate"/> inclus.</summary>
public record CreateAbsenceJustificationCommand : IRequest<Guid>
{
    public Guid StudentId { get; init; }
    public DateOnly StartDate { get; init; }

    /// <summary>Dernier jour d'absence ; absent = absence d'un seul jour.</summary>
    public DateOnly? EndDate { get; init; }
    public string Reason { get; init; } = null!;
    public DateOnly? AuthorizedReturnDate { get; init; }
}
