using MediatR;
using RequestStatus = SamaEcole.Domain.Enums.RegistrationRequestStatus;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Registration.Queries.GetRegistrationRequests;

/// <summary>
/// Ticket JGK-I03 — liste des demandes d'inscription pour la revue du Super Admin, filtrable par statut.
/// Le Super Admin n'a aucun schoolId, mais `school_registration_requests` est une table plateforme hors
/// RLS : il les voit donc toutes (c'est justement son rôle de les arbitrer).
/// </summary>
public record GetRegistrationRequestsQuery(RequestStatus? Status)
    : IRequest<IReadOnlyList<RegistrationRequestListItem>>;

/// <summary>
/// Tout ce dont le Super Admin a besoin pour qualifier une demande — mais JAMAIS le hash du mot de passe
/// (<c>DirectorPasswordHash</c> n'est pas projeté ici). L'e-mail/téléphone du Directeur, eux, sont
/// nécessaires à la revue : le Super Admin est l'arbitre légitime, contrairement au suivi public (I02).
/// </summary>
public record RegistrationRequestListItem(
    Guid Id,
    string TrackingReference,
    string SchoolName,
    string? SchoolAddress,
    string? City,
    string? Region,
    int? EstimatedStudentCount,
    SubscriptionPlan RequestedPlan,
    string DirectorFullName,
    string DirectorEmail,
    string DirectorPhone,
    RequestStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string? RejectionReason,
    Guid? CreatedSchoolId);
