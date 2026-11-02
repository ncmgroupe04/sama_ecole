using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Session de pointage configurée par la Direction : « Appel de nuit », « Déjeuner », « Appel de la Halqa »… Rien n'est imposé
/// hors la session « Appel de nuit » reprise par la migration pour les écoles qui pointaient déjà. Le <see cref="TimeLabel"/>
/// est un simple repère affiché (« 19 h 30 »), jamais une règle ; <see cref="Days"/> signale un jour hors sélection sans le
/// bloquer.
/// </summary>
public class AttendanceSession : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    public AttendanceAudience Audience { get; set; }

    /// <summary>Jours concernés, un bit par jour (<see cref="AttendanceDays"/>). Tous les jours par défaut.</summary>
    public int Days { get; set; } = AttendanceDays.All;

    public string? TimeLabel { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}

/// <summary>Jours de la semaine d'une session, en masque de bits : lundi = 1 … dimanche = 64 (ISO 8601, 1 à 7 côté API).</summary>
public static class AttendanceDays
{
    public const int All = 127;

    /// <summary>Masque d'un jour ISO (1 = lundi … 7 = dimanche).</summary>
    public static int BitOfIsoDay(int isoDay) => 1 << (isoDay - 1);

    public static int IsoDayOf(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    public static bool Includes(int mask, DateOnly date) => (mask & BitOfIsoDay(IsoDayOf(date))) != 0;

    public static int ToMask(IEnumerable<int> isoDays) => isoDays.Aggregate(0, (mask, day) => mask | BitOfIsoDay(day));

    public static IReadOnlyList<int> ToIsoDays(int mask) =>
        Enumerable.Range(1, 7).Where(day => (mask & BitOfIsoDay(day)) != 0).ToList();
}
