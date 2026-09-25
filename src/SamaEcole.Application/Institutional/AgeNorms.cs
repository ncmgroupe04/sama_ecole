using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Institutional;

/// <summary>Tranche d'âge normale d'un niveau (âges révolus à la date de référence, bornes incluses).</summary>
public sealed record AgeNorm(string GradeLevel, CycleType Cycle, int MinAge, int MaxAge);

/// <summary>Position d'un élève par rapport à la tranche d'âge de son niveau.</summary>
public enum AgeNormStatus
{
    /// <summary>Niveau non reconnu ou date de naissance invraisemblable : rien à juger.</summary>
    Unknown,
    Early,
    Normal,
    Late
}

/// <summary>
/// Tranches d'âge par niveau selon les normes du Ministère (Évolution N°7) — le MODÈLE national, en code comme les
/// modèles de coefficients (arbitrage A10) : âge « normal » d'un niveau (CI à 6 ans … Terminale à 18 ans), avec
/// une tolérance d'un an d'avance et de deux ans de retard. L'école peut s'en écarter niveau par niveau
/// (table grade_age_norms) ; sans ligne, le modèle s'applique. Les âges sont RÉVOLUS au 31 décembre de l'année de
/// rentrée — la convention des statistiques scolaires, qui rend l'âge indépendant du jour de l'inscription.
/// </summary>
public static class AgeNormTemplates
{
    public const int Advance = 1;
    public const int Delay = 2;

    /// <summary>Âge normal de chaque niveau, dans l'ordre de la nomenclature ClassroomGradeLevels.</summary>
    public static readonly IReadOnlyList<(string Grade, CycleType Cycle, int NormalAge)> NormalAges =
    [
        ("TPS", CycleType.Maternelle, 2), ("PS", CycleType.Maternelle, 3), ("MS", CycleType.Maternelle, 4), ("GS", CycleType.Maternelle, 5),
        ("CI", CycleType.Primaire, 6), ("CP", CycleType.Primaire, 7), ("CE1", CycleType.Primaire, 8),
        ("CE2", CycleType.Primaire, 9), ("CM1", CycleType.Primaire, 10), ("CM2", CycleType.Primaire, 11),
        ("Sixième", CycleType.College, 12), ("Cinquième", CycleType.College, 13),
        ("Quatrième", CycleType.College, 14), ("Troisième", CycleType.College, 15),
        ("Seconde", CycleType.Lycee, 16), ("Première", CycleType.Lycee, 17), ("Terminale", CycleType.Lycee, 18)
    ];

    public static IReadOnlyList<AgeNorm> All { get; } = NormalAges
        .Select(n => new AgeNorm(n.Grade, n.Cycle, Math.Max(0, n.NormalAge - Advance), n.NormalAge + Delay))
        .ToList();

    public static AgeNorm? For(string? grade) => All.FirstOrDefault(n => n.GradeLevel == grade);
}

public static class AgeRules
{
    public const int MaxPlausibleAge = 30;

    /// <summary>Date de référence des âges d'une année scolaire : le 31 décembre de l'année de rentrée.</summary>
    public static DateOnly ReferenceDate(DateOnly schoolYearStart) => new(schoolYearStart.Year, 12, 31);

    /// <summary>Âge révolu à <paramref name="on"/> ; null pour une date de naissance future ou invraisemblable.</summary>
    public static int? AgeAt(DateOnly birthDate, DateOnly on)
    {
        if (birthDate > on)
        {
            return null;
        }

        var age = on.Year - birthDate.Year;
        if (birthDate > on.AddYears(-age))
        {
            age--;
        }

        return age is >= 0 and <= MaxPlausibleAge ? age : null;
    }

    public static AgeNormStatus Classify(int? age, AgeNorm? norm) => (age, norm) switch
    {
        (null, _) or (_, null) => AgeNormStatus.Unknown,
        ({ } a, { } n) when a < n.MinAge => AgeNormStatus.Early,
        ({ } a, { } n) when a > n.MaxAge => AgeNormStatus.Late,
        _ => AgeNormStatus.Normal
    };

    /// <summary>Niveau d'une classe (« CM2 A » → « CM2 »), ou null si son nom ne le dit pas.</summary>
    public static string? GradeOf(string classroomName, CycleType cycle) =>
        ClassroomGradeLevels.FromClassroomName(classroomName, cycle);

    /// <summary>
    /// Tranches de l'école courante : le modèle national, remplacé niveau par niveau par les réglages de l'école
    /// (Global Query Filter + RLS : jamais ceux d'une autre école).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, AgeNorm>> ResolveAsync(
        IApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var custom = await dbContext.GradeAgeNorms.AsNoTracking()
            .Select(n => new { n.GradeLevel, n.MinAge, n.MaxAge })
            .ToListAsync(cancellationToken);

        return AgeNormTemplates.All.ToDictionary(
            n => n.GradeLevel,
            n => custom.FirstOrDefault(c => c.GradeLevel == n.GradeLevel) is { } c
                ? n with { MinAge = c.MinAge, MaxAge = c.MaxAge }
                : n);
    }
}

/// <summary>Statut d'un élève au rapport de rentrée IEF.</summary>
public enum StudentEntryStatus
{
    New,
    Repeater,
    Transferred
}

public static class StudentEntryStatuses
{
    /// <summary>Redoublant d'abord (colonne réglementaire), puis Transféré, sinon Nouveau.</summary>
    public static StudentEntryStatus Of(bool isRepeating, bool isTransferredIn) =>
        isRepeating ? StudentEntryStatus.Repeater
        : isTransferredIn ? StudentEntryStatus.Transferred
        : StudentEntryStatus.New;
}

/// <summary>Une colonne d'âge du rapport : un âge, ou une borne ouverte (« ≤ 10 », « ≥ 20 »).</summary>
public sealed record AgeBucket(string Label, int? From, int? To)
{
    public bool Contains(int? age) => age is { } a && (From is null || a >= From) && (To is null || a <= To);
}

public static class AgeBuckets
{
    /// <summary>
    /// Colonnes d'âge d'un tableau : un âge par colonne entre le plus jeune et le plus âgé, les extrêmes regroupés
    /// (« ≤ x », « ≥ y ») pour ne pas dépasser <paramref name="maxColumns"/> — un tableau de page A4 paysage.
    /// </summary>
    public static IReadOnlyList<AgeBucket> Build(IEnumerable<int?> ages, int maxColumns = 12)
    {
        var known = ages.Where(a => a is not null).Select(a => a!.Value).Distinct().Order().ToList();
        if (known.Count == 0)
        {
            return [];
        }

        int low = known[0], high = known[^1];
        if (high - low + 1 <= maxColumns)
        {
            return Enumerable.Range(low, high - low + 1).Select(a => new AgeBucket($"{a} ans", a, a)).ToList();
        }

        // Garde la plage la plus peuplée au centre : les âges médians, extrêmes regroupés de part et d'autre.
        var median = known[known.Count / 2];
        var first = Math.Max(low + 1, median - (maxColumns - 2) / 2);
        var last = first + maxColumns - 3;
        if (last >= high)
        {
            last = high - 1;
            first = last - (maxColumns - 3);
        }

        var buckets = new List<AgeBucket> { new($"≤ {first - 1} ans", null, first - 1) };
        buckets.AddRange(Enumerable.Range(first, last - first + 1).Select(a => new AgeBucket($"{a} ans", a, a)));
        buckets.Add(new AgeBucket($"≥ {last + 1} ans", last + 1, null));
        return buckets;
    }
}
