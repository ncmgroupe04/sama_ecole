using System.Globalization;

namespace SamaEcole.Application.RollBook;

/// <summary>Lecture du paramètre « mois » (AAAA-MM) du cahier. Pur.</summary>
public static class RollBookPeriods
{
    /// <summary>Premier et dernier jour du mois ; faux si la chaîne n'est pas exactement « AAAA-MM ».</summary>
    public static bool TryParseMonth(string? month, out DateOnly first, out DateOnly last)
    {
        first = default;
        last = default;

        if (month is not { Length: 7 }
            || !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
        {
            return false;
        }

        last = first.AddMonths(1).AddDays(-1);
        return true;
    }
}
