using System.Collections.Immutable;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Lien entre un EFFECTIF et les tranches d'effectif, SANS accès base de données (testable à plat).
///
/// Sert la règle anti-fraude : un directeur ne peut pas choisir une tranche dont le plafond nominal est
/// inférieur à l'effectif qu'il déclare (<see cref="IsSufficientFor"/>). L'effectif RÉEL, lui, n'est jamais
/// déclaratif : le quota compte les élèves vivants (StudentQuotaGuard).
///
/// Au-delà de la dernière tranche standard (400), il n'y a plus de prix publié : <see cref="RequiresQuote"/>
/// (offre « Grand Groupe », sur devis).
/// </summary>
public static class QuotaTierResolver
{
    /// <summary>Toutes les tranches, du plafond nominal le plus bas au plus haut. L'ordre de l'enum, lui, ne signifie rien.</summary>
    public static ImmutableArray<StudentQuotaTier> Ladder { get; } =
    [
        StudentQuotaTier.Tier_100,
        StudentQuotaTier.Tier1_150,
        StudentQuotaTier.Tier_250,
        StudentQuotaTier.Tier2_400,
        StudentQuotaTier.Tier3_800,
        StudentQuotaTier.Tier4_Custom
    ];

    /// <summary>Plus petite tranche dont le plafond nominal contient l'effectif ; au-delà de 800, la tranche sur mesure.</summary>
    public static StudentQuotaTier MinimumTierFor(int studentCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(studentCount);

        foreach (var tier in Ladder)
        {
            if (studentCount <= StudentQuotaDefaults.For(tier).Max)
            {
                return tier;
            }
        }

        return StudentQuotaTier.Tier4_Custom;
    }

    /// <summary>Vrai si la tranche choisie est au moins la tranche minimale de l'effectif déclaré.</summary>
    public static bool IsSufficientFor(StudentQuotaTier chosen, int declaredStudentCount) =>
        Rank(chosen) >= Rank(MinimumTierFor(declaredStudentCount));

    /// <summary>Vrai pour les tranches sans prix publié : tout ce qui dépasse la dernière tranche standard.</summary>
    public static bool RequiresQuote(StudentQuotaTier tier) =>
        Rank(tier) > Rank(StudentQuotaTier.Tier2_400);

    private static int Rank(StudentQuotaTier tier)
    {
        var index = Ladder.IndexOf(tier);

        return index >= 0
            ? index
            : throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tranche d'effectif inconnue.");
    }
}
