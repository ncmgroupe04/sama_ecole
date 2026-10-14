using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Demande de changement d'abonnement d'un établissement : changer de profil, ajouter ou retirer un cycle, passer à une
/// autre tranche d'effectif, activer un module non souscrit. Le Directeur ne modifie JAMAIS ces choix directement dans
/// ses paramètres : ses tentatives sont interceptées et deviennent une demande, que le Super Admin valide (ou rejette)
/// une fois le règlement convenu. Rien n'est écrit sur la souscription ni sur les réglages avant cette validation.
///
/// La demande porte l'état COMPLET avant et après (profil, tranche, cycles, modules, internat) plutôt qu'un simple
/// « delta » : le Super Admin voit exactement ce qui change, et la validation n'a qu'à appliquer l'état cible — le calcul
/// (preset du profil, plafonds de la tranche) est fait UNE fois, à la demande, côté application.
///
/// Table TENANT (RLS + Global Query Filter) : le Directeur lit et crée les demandes de SON école. Les lectures et la
/// décision du Super Admin, qui n'a aucun SchoolId de session, passent par des fonctions SECURITY DEFINER, comme pour
/// <see cref="TenantSubscription"/>.
/// </summary>
public class SubscriptionChangeRequest : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public SubscriptionChangeStatus Status { get; set; } = SubscriptionChangeStatus.Pending;

    /// <summary>
    /// Vrai si la demande réduit l'abonnement : tranche inférieure, cycle retiré, module souscrit qui disparaît. Le
    /// Directeur ne peut pas se l'appliquer lui-même (risque d'effacement involontaire de classes et d'élèves) : elle est
    /// traitée par le support, qui voit ce drapeau.
    /// </summary>
    public bool IsDowngrade { get; set; }

    // ------------------------------------------------------------------ État AU MOMENT de la demande

    public ProfileType CurrentProfile { get; set; }
    public StudentQuotaTier CurrentTier { get; set; }

    /// <summary>Cycles gérés (« Maternelle,Primaire,… », forme canonique de ManagedCycleSet).</summary>
    public string CurrentCycles { get; set; } = string.Empty;

    public bool CurrentHasBoarding { get; set; }
    public bool CurrentIsPedagogyEnabled { get; set; }
    public bool CurrentIsFinanceEnabled { get; set; }
    public bool CurrentIsInternatEnabled { get; set; }
    public bool CurrentIsCoranModuleEnabled { get; set; }

    // ------------------------------------------------------------------ État DEMANDÉ (ce que la validation écrit)

    public ProfileType TargetProfile { get; set; }

    /// <summary>Profil d'Onboarding historique correspondant (<c>school_settings.ProfileEtablissement</c>).</summary>
    public ProfileEtablissement TargetEstablishmentProfile { get; set; }

    public StudentQuotaTier TargetTier { get; set; }
    public int TargetMaxStudentLimit { get; set; }
    public int TargetSoftQuotaLimit { get; set; }
    public string TargetCycles { get; set; } = string.Empty;
    public bool TargetHasBoarding { get; set; }
    public bool TargetIsPedagogyEnabled { get; set; }
    public bool TargetIsFinanceEnabled { get; set; }
    public bool TargetIsInternatEnabled { get; set; }
    public bool TargetIsCoranModuleEnabled { get; set; }

    // ------------------------------------------------------------------ Tarif, tel qu'annoncé au Directeur

    /// <summary>
    /// Tarifs de la grille par tranche à la date de la demande, FCFA. NULL quand la tranche est sur devis (« Grand Groupe »)
    /// ou quand l'école suit encore l'ancienne grille : l'écart n'est alors pas calculable, et le support le chiffre.
    /// </summary>
    public decimal? CurrentMonthlyXof { get; set; }
    public decimal? CurrentAnnualXof { get; set; }
    public decimal? TargetMonthlyXof { get; set; }
    public decimal? TargetAnnualXof { get; set; }

    // ------------------------------------------------------------------ Demandeur et décision

    public Guid RequestedBy { get; set; }

    /// <summary>
    /// Nom et adresse du demandeur, FIGÉS à la demande : la table des comptes est sous RLS, le Super Admin n'a aucun SchoolId de
    /// session pour la lire, et c'est à cette adresse que part l'e-mail de décision (validation ou rejet). NULL pour une demande
    /// qui n'a pas pu les consigner : la décision s'applique alors sans e-mail.
    /// </summary>
    public string? RequestedByName { get; set; }

    public string? RequestedByEmail { get; set; }

    /// <summary>Précision facultative du Directeur (« nous ouvrons un internat en janvier »).</summary>
    public string? DirectorNote { get; set; }

    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>Motif d'un rejet (obligatoire) ou remarque de validation.</summary>
    public string? ReviewNote { get; set; }
}
