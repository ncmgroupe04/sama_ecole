using System.Linq.Expressions;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Domain.Enums;

/// <summary>
/// Statut d'une sortie (spec N6) : jamais stocké, calculé à partir des dates. La fonction pure et les prédicats
/// d'expression disent la même chose — les seconds se traduisent en SQL pour le filtre de la liste.
/// </summary>
public static class BoardingLeaveStatus
{
    public static LeaveStatus Of(DateOnly leaveDate, DateOnly expectedReturnDate, DateOnly? actualReturnDate, DateOnly today)
    {
        if (actualReturnDate is not null)
        {
            return LeaveStatus.Returned;
        }

        if (today < leaveDate)
        {
            return LeaveStatus.Pending;
        }

        return today > expectedReturnDate ? LeaveStatus.Overdue : LeaveStatus.Active;
    }

    public static LeaveStatus Of(BoardingLeave leave, DateOnly today) =>
        Of(leave.LeaveDate, leave.ExpectedReturnDate, leave.ActualReturnDate, today);

    /// <summary>Même verdict que <see cref="Of(BoardingLeave, DateOnly)"/>, traduisible en SQL.</summary>
    public static Expression<Func<BoardingLeave, bool>> Matches(LeaveStatus status, DateOnly today) => status switch
    {
        LeaveStatus.Returned => l => l.ActualReturnDate != null,
        LeaveStatus.Pending => l => l.ActualReturnDate == null && today < l.LeaveDate,
        LeaveStatus.Overdue => l => l.ActualReturnDate == null && today >= l.LeaveDate && today > l.ExpectedReturnDate,
        _ => l => l.ActualReturnDate == null && today >= l.LeaveDate && today <= l.ExpectedReturnDate
    };

    /// <summary>
    /// Le pensionnaire dort dehors la nuit <paramref name="night"/> : parti, et pas encore rentré CE soir-là. Une sortie
    /// dépassée mais non rentrée compte encore ; la nuit du retour, il dort à l'internat.
    /// </summary>
    public static Expression<Func<BoardingLeave, bool>> OnNight(DateOnly night) =>
        l => l.LeaveDate <= night && (l.ActualReturnDate == null || l.ActualReturnDate > night);
}
