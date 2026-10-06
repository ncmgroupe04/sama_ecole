using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Internat;

/// <summary>
/// Règles du suivi coranique par Hizb (module Internat/Daara), partagées par les commandes, les requêtes et
/// leurs validateurs. Les mêmes bornes sont tenues en base par des contraintes CHECK
/// (StudentHizbStatusConfiguration) : cette classe en est le miroir applicatif, pour qu'un client reçoive un 422
/// lisible plutôt qu'une violation de contrainte.
/// </summary>
public static class HizbRules
{
    public const int HizbCount = 60;
    public const int QuartersPerHizb = 4;

    /// <summary>Le Coran compte 60 Hizb de 4 quarts : 240 quarts pour une mémorisation complète.</summary>
    public const int TotalQuarters = HizbCount * QuartersPerHizb;

    public const int MinRating = 1;
    public const int MaxRating = 5;

    /// <summary>Borne d'une affectation en lot : une Halqa tient en quelques dizaines d'élèves, jamais en milliers.</summary>
    public const int MaxStudentsPerAssignment = 200;

    /// <summary>
    /// L'état se DÉDUIT des quarts, jamais l'inverse : 0 = non commencé, 1 à 3 = en cours, 4 = complet.
    /// Le client n'envoie donc jamais d'état (une seule source de vérité, comme la contrainte CHECK en base).
    /// </summary>
    public static HizbMemorizationState StateFor(int completedQuarters) => completedQuarters switch
    {
        <= 0 => HizbMemorizationState.NotStarted,
        >= QuartersPerHizb => HizbMemorizationState.Completed,
        _ => HizbMemorizationState.InProgress
    };

    /// <summary>Pourcentage de mémorisation globale (quarts acquis / 240), arrondi à une décimale.</summary>
    public static decimal ProgressPercent(int completedQuarters) =>
        Math.Round(completedQuarters * 100m / TotalQuarters, 1, MidpointRounding.AwayFromZero);
}
