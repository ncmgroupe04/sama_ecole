using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Users.Commands.ChangeUserStatus;

/// <summary>
/// PATCH /users/{userId}/status — ticket JGK-A05 (openapi.yaml).
/// Le motif est obligatoire : un blocage sans justification n'est pas auditable.
/// </summary>
public record ChangeUserStatusCommand(Guid UserId, EntityStatus Status, string Reason)
    : IRequest<ChangeUserStatusResult>;

public record ChangeUserStatusResult(
    Guid UserId,
    EntityStatus PreviousStatus,
    EntityStatus NewStatus,
    int RevokedSessions);
