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

    public SubscriptionPlan RequestedPlan { get; init; }
}

/// <summary>
/// La référence de suivi est la SEULE donnée renvoyée : ni identifiant interne, ni écho du mot de passe.
/// C'est elle que le Directeur utilisera pour suivre sa demande (ticket JGK-I02).
/// </summary>
public record SubmitRegistrationRequestResult(string TrackingReference);
