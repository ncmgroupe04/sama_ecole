using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Absences.Commands.CreateLateArrival;

public record CreateLateArrivalCommand : IRequest<Guid>, IAuditableRequest
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

    /// <summary>
    /// Heure d'arrivée réelle de l'élève (Complément N°5 bis). Quand elle est fournie, le SERVEUR en déduit les cours
    /// manqués, le retard sur le cours en cours, le cours visé et la durée totale : <see cref="Minutes"/> et
    /// <see cref="TargetScheduleSlotId"/> sont alors IGNORÉS. Absente, le billet se saisit à l'ancienne.
    /// </summary>
    public TimeOnly? ArrivalTime { get; init; }
}
