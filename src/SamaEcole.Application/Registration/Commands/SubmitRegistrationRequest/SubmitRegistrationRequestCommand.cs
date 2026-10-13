using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Registration.Commands.SubmitRegistrationRequest;

/// <summary>
/// Ticket JGK-I01 — soumission du formulaire PUBLIC d'inscription self-service (docs/Volume_1_Cahier_des_Charges.md
/// §11.5). Anonyme : aucun compte, aucune école, aucun abonnement n'existe encore à ce stade.
///
/// Le mot de passe est fourni en clair UNIQUEMENT ici (requête HTTPS) et haché immédiatement par le Handler ;
/// il n'est jamais stocké ni renvoyé, et l'e-mail de confirmation ne transmet QUE la référence de suivi
/// (docs/Volume_7_Security.md §Paiements). La référence de suivi est générée par le Handler, jamais fournie
/// par l'appelant (même règle que le matricule/reçu — AGENTS.md règle #3).
/// </summary>
public record SubmitRegistrationRequestCommand : IRequest<SubmitRegistrationRequestResult>
{
    public required string DirectorFullName { get; init; }
    public required string DirectorEmail { get; init; }
    public required string DirectorPhone { get; init; }
    public required string DirectorPassword { get; init; }

    public required string SchoolName { get; init; }
    public string? SchoolAddress { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public int? EstimatedStudentCount { get; init; }

    /// <summary>
    /// Profil de l'établissement, dans les termes de la grille tarifaire de la vitrine. Nullables pour pouvoir
    /// EXIGER un choix explicite (un enum non nullable retomberait silencieusement sur sa première valeur).
    /// Le plan d'abonnement n'est plus saisi : il est déduit du profil (SubscriptionPricingGrid.PlanFor).
    /// </summary>
    public SchoolOwnership? Ownership { get; init; }
    public SchoolCycleProfile? CycleProfile { get; init; }

    /// <summary>
    /// Palier de taille de la grille HISTORIQUE — obligatoire pour un établissement privé qui ne choisit pas de tranche
    /// d'effectif, ignoré (mis à null) pour un public et pour une offre par tranche.
    /// </summary>
    public SchoolSizeTier? SizeTier { get; init; }

    /// <summary>
    /// OFFRE PAR TRANCHE D'EFFECTIF (établissements privés) : profil d'enseignement, internat, cycles cochés et tranche.
    /// <see cref="Profile"/> et <see cref="Tier"/> vont ensemble ; renseignés, ils remplacent <see cref="CycleProfile"/> (déduit
    /// des cycles cochés) et <see cref="SizeTier"/>. <see cref="EstimatedStudentCount"/> devient alors obligatoire : c'est lui
    /// qui borne la tranche (anti-fraude). Le serveur recalcule tout montant ; aucun prix ne vient du client.
    /// </summary>
    public ProfileType? Profile { get; init; }

    /// <summary>L'école héberge des élèves (Internat) — active le module et la majoration Internat de la grille.</summary>
    public bool HasBoarding { get; init; }

    /// <summary>Cycles académiques gérés ; obligatoire pour un profil académique, facultatif pour un Daara de mémorisation ou une Comptabilité.</summary>
    public IReadOnlyList<CycleType>? Cycles { get; init; }

    public StudentQuotaTier? Tier { get; init; }
}

/// <summary>
/// La référence de suivi est la SEULE donnée renvoyée : ni identifiant interne, ni écho du mot de passe.
/// C'est elle que le Directeur utilisera pour suivre sa demande (ticket JGK-I02).
/// </summary>
public record SubmitRegistrationRequestResult(string TrackingReference);
