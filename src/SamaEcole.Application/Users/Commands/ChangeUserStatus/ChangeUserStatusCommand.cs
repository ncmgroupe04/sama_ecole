using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Users.Commands.ChangeUserStatus;

/// <summary>
/// PATCH /users/{userId}/status — ticket JGK-A05 (openapi.yaml).
/// Le motif est obligatoire : un blocage sans justification n'est pas auditable.
///
/// IAuditableRequest (JGK-H01) : « changements de statut utilisateur » fait partie des écritures
/// sensibles explicitement listées par le journal d'audit centralisé — en plus de son propre historique
/// dédié (UserStatusHistory), qui reste la vue détaillée PAR compte.
/// </summary>
public record ChangeUserStatusCommand(Guid UserId, EntityStatus Status, string Reason)
    : IRequest<ChangeUserStatusResult>, IAuditableRequest;

public record ChangeUserStatusResult(
    Guid UserId,
    EntityStatus PreviousStatus,
    EntityStatus NewStatus,
    int RevokedSessions);
