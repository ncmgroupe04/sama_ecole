using System.Globalization;
using FluentValidation.Results;
using SamaEcole.Application.Common.Exceptions;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>Lecture du paramètre <c>month</c> (« AAAA-MM ») des récapitulatifs mensuels : le premier jour du mois, ou 422.</summary>
public static class AttendanceMonth
{
    public static DateOnly Parse(string? month)
    {
        if (!DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            throw new ValidationException([new ValidationFailure("Month", "Indiquez un mois au format AAAA-MM (ex. 2026-10).")]);
        }

        return start;
    }
}
