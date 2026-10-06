using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Souscription commerciale d'un établissement : profil choisi, tranche d'effectif et plafonds d'élèves,
/// modules activés. Une seule ligne vivante par école.
///
/// C'est une table TENANT (ITenantEntity + policy RLS) pour la même raison que <see cref="Subscription"/> :
/// elle est rattachée à UNE école et lue en permanence par ses utilisateurs (quota à la création d'un
/// élève, écran d'Onboarding). Les écritures du Super Admin — qui n'a aucun SchoolId de session — passent
/// par une fonction SECURITY DEFINER, ajoutée avec la commande de changement de tranche.
///
/// Distincte de <see cref="Subscription"/> : celle-ci est l'abonnement de FACTURATION (formule
/// Primaire/Standard/Premium, expiration, paiements) ; celle-là est la configuration produit. Aucune des
/// deux ne remplace l'autre.
///
/// Les quatre modules (<see cref="IsPedagogyEnabled"/>…) reflètent <see cref="SchoolSettings"/> à la date de
/// reprise. Tant que <c>ModuleAuthorizationHandler</c> et la sidebar lisent <see cref="SchoolSettings"/>,
/// ce sont ces derniers qui font foi pour l'affichage : ces drapeaux ne pilotent encore rien.
/// </summary>
public class TenantSubscription : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public ProfileType ProfileType { get; set; }

    public StudentQuotaTier StudentQuotaTier { get; set; }

    /// <summary>Nombre d'élèves inclus dans la tranche (plafond « nominal »).</summary>
    public int MaxStudentLimit { get; set; }

    /// <summary>
    /// Plafond de tolérance : entre <see cref="MaxStudentLimit"/> (exclu) et cette valeur (incluse), la
    /// création d'élèves reste permise avec un avertissement ; au-delà, elle est refusée.
    /// Toujours supérieur ou égal à <see cref="MaxStudentLimit"/>.
    /// </summary>
    public int SoftQuotaLimit { get; set; }

    public TenantSubscriptionStatus Status { get; set; } = TenantSubscriptionStatus.PendingOnboarding;

    public bool IsPedagogyEnabled { get; set; }
    public bool IsFinanceEnabled { get; set; }
    public bool IsInternatEnabled { get; set; }
    public bool IsCoranModuleEnabled { get; set; }
}

/// <summary>
/// Plafonds par tranche d'effectif. Source unique de ces nombres : l'entité, le service de quota et la
/// migration de reprise (qui les recopie en dur, une migration ne devant jamais dépendre du code vivant)
/// s'y réfèrent.
/// </summary>
public static class StudentQuotaDefaults
{
    /// <summary>Plafond « illimité » — valeur des écoles existantes et de la tranche sur mesure non fixée.</summary>
    public const int Unlimited = int.MaxValue;

    /// <summary>Plafonds (nominal, tolérance) d'une tranche. La tranche sur mesure est illimitée par défaut.</summary>
    public static (int Max, int Soft) For(StudentQuotaTier tier) => tier switch
    {
        StudentQuotaTier.Tier1_150 => (150, 160),
        StudentQuotaTier.Tier2_400 => (400, 420),
        StudentQuotaTier.Tier3_800 => (800, 830),
        StudentQuotaTier.Tier4_Custom => (Unlimited, Unlimited),
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tranche d'effectif inconnue.")
    };

    /// <summary>
    /// Plafonds d'une tranche sur mesure fixée par le Super Admin. Tolérance CALCULÉE : 4 % du plafond,
    /// au moins 10 élèves, sans jamais dépasser <see cref="Unlimited"/>. Choix de départ, pas une règle
    /// du cahier des charges — le Super Admin pourra surcharger la valeur le jour où l'écran existera.
    /// </summary>
    public static (int Max, int Soft) ForCustom(int maxStudentLimit)
    {
        if (maxStudentLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxStudentLimit), maxStudentLimit, "Le plafond doit être d'au moins 1 élève.");
        }

        var margin = Math.Max(10, (int)Math.Ceiling(maxStudentLimit * 0.04));
        var soft = (long)maxStudentLimit + margin;

        return (maxStudentLimit, soft >= Unlimited ? Unlimited : (int)soft);
    }
}
