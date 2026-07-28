using MediatR;
using RequestStatus = SamaEcole.Domain.Enums.RegistrationRequestStatus;

namespace SamaEcole.Application.Registration.Queries.GetRegistrationRequestStatus;

/// <summary>
/// Ticket JGK-I02 — GET /api/v1/registration-requests/{trackingReference}/status. Anonyme, comme la
/// soumission (JGK-I01) : le Directeur n'a encore ni compte ni session à ce stade, la référence de
/// suivi EST son seul moyen d'authentification de fait auprès de cette ressource.
/// </summary>
public record GetRegistrationRequestStatusQuery(string TrackingReference)
    : IRequest<GetRegistrationRequestStatusResult>;

/// <summary>
/// STRICT MINIMUM exposé publiquement (docs/Volume_7_Security.md — une ressource accessible sans
/// authentification ne doit jamais fuiter plus que ce que son usage exige) : ni e-mail, ni téléphone,
/// ni hash de mot de passe, ni adresse, ni effectif, ni plan souhaité, ni identité du Super Admin
/// ayant statué. <see cref="RejectionReason"/> est l'exception délibérée à la sobriété : c'est le SEUL
/// canal par lequel un Directeur rejeté apprend jamais le motif (JGK-I03 l'exige "obligatoire" côté
/// Super Admin) — l'omettre rendrait cette exigence inutile. Il reste NULL hors statut Rejected.
/// </summary>
public record GetRegistrationRequestStatusResult(
    string TrackingReference,
    string SchoolName,
    RequestStatus Status,
    DateTimeOffset SubmittedAt,
    string? RejectionReason);
