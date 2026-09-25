using MediatR;

namespace SamaEcole.Application.Absences.Commands.CreateLateArrival;

public record CreateLateArrivalCommand : IRequest<Guid>
{
    public Guid StudentId { get; init; }
    public DateTime Date { get; init; }
    public int Minutes { get; init; }
    public string Reason { get; init; } = null!;
    public string? Observations { get; init; }

    /// <summary>
    /// Cours d'emploi du temps que l'élève rejoint (Évolution N°5) : le retard devient un BILLET D'ENTRÉE qui met à
    /// jour le registre d'appel de ce cours. Absent, le comportement est celui d'avant (aucun cours visé).
    /// </summary>
    public Guid? TargetScheduleSlotId { get; init; }
}
