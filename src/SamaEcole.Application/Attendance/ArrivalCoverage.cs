using FluentValidation.Results;
using SamaEcole.Application.Common.Exceptions;

namespace SamaEcole.Application.Attendance;

/// <summary>Un cours de la classe pour la journée : ce qu'il faut savoir de lui pour situer une arrivée.</summary>
public readonly record struct ArrivalSlot(Guid SlotId, TimeOnly Start, TimeOnly End);

/// <summary>
/// Ce qu'une heure d'arrivée dit de la journée de l'élève (Complément N°5 bis).
/// <c>TargetSlotId</c> = le cours que l'enseignant acceptera (le billet le vise).
/// </summary>
public sealed record ArrivalCoverageResult(
    IReadOnlyList<Guid> MissedSlotIds,
    Guid? InProgressSlotId,
    int LateMinutes,
    int MissedMinutes,
    int TotalMinutes,
    Guid? TargetSlotId);

/// <summary>
/// Calcul PUR (arbitrages C3/C4) : le surveillant ne saisit que l'heure d'arrivée réelle, le serveur en déduit
/// <list type="bullet">
/// <item>les cours MANQUÉS : ceux qui étaient déjà terminés (<c>End &lt;= arrivée</c>) ;</item>
/// <item>le cours EN COURS : <c>Start &lt; arrivée &lt; End</c>, dont le retard est <c>arrivée − Start</c> ;</item>
/// <item>les autres (<c>Start &gt;= arrivée</c>) sont ignorés : l'élève y est à l'heure.</item>
/// </list>
/// Durée totale = somme des cours manqués + minutes de retard. Le cours visé est le cours en cours, à défaut le
/// prochain (arrivée pendant une pause), à défaut le dernier cours manqué (plus aucun cours ce jour-là).
/// Sans base de données ni horloge : testable seule, et le client n'y refait jamais le calcul.
/// </summary>
public static class ArrivalCoverage
{
    public static ArrivalCoverageResult Compute(IReadOnlyList<ArrivalSlot> slots, TimeOnly arrival)
    {
        if (slots.Count == 0)
        {
            throw Refused("Aucun cours n'est prévu pour cette classe ce jour-là : saisissez directement les minutes de retard.");
        }

        var ordered = slots.OrderBy(s => s.Start).ThenBy(s => s.End).ToList();

        if (arrival < ordered[0].Start)
        {
            throw Refused($"Aucun cours n'a commencé à cette heure-là : le premier cours débute à {ordered[0].Start:HH\\:mm}.");
        }

        var missed = ordered.Where(s => s.End <= arrival).ToList();
        var inProgress = ordered.FirstOrDefault(s => s.Start < arrival && arrival < s.End);
        var hasInProgress = inProgress.SlotId != Guid.Empty;

        var lateMinutes = hasInProgress ? (int)(arrival - inProgress.Start).TotalMinutes : 0;
        var missedMinutes = missed.Sum(s => (int)(s.End - s.Start).TotalMinutes);

        if (missed.Count == 0 && lateMinutes == 0)
        {
            throw Refused("Rien à régulariser : l'élève arrive à l'heure du premier cours.");
        }

        Guid? target = hasInProgress
            ? inProgress.SlotId
            : ordered.Where(s => s.Start >= arrival).Select(s => (Guid?)s.SlotId).FirstOrDefault()
              ?? missed[^1].SlotId;

        return new ArrivalCoverageResult(
            missed.Select(s => s.SlotId).ToList(),
            hasInProgress ? inProgress.SlotId : null,
            lateMinutes,
            missedMinutes,
            missedMinutes + lateMinutes,
            target);
    }

    private static ValidationException Refused(string message)
        => new([new ValidationFailure("ArrivalTime", message)]);
}
