using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Users.Commands.ChangeUserStatus;

/// <summary>
/// Ticket JGK-A05 — suspension / blocage / réactivation d'un compte.
///
/// Suspendre un compte sans couper ses sessions ne suspend rien : le JWT déjà émis reste valable
/// jusqu'à 15 minutes, et surtout son refresh token reste valable JUSQU'À 14 JOURS. Un utilisateur
/// bloqué pourrait donc continuer à travailler deux semaines. On révoque donc explicitement tous ses
/// refresh tokens — c'est la partie du ticket qui n'est pas dans son libellé, mais sans laquelle
/// « bloqué » ne veut rien dire.
///
/// L'access token en cours (≤ 15 min) n'est, lui, pas révocable — c'est le prix d'un JWT sans état.
/// La fenêtre est bornée et acceptée (docs/Volume_4_API_Design.md §1.1).
/// </summary>
public class ChangeUserStatusCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<ChangeUserStatusCommandHandler> logger)
    : IRequestHandler<ChangeUserStatusCommand, ChangeUserStatusResult>
{
    public async Task<ChangeUserStatusResult> Handle(ChangeUserStatusCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Un Directeur ne doit pas pouvoir se bloquer lui-même : plus personne ne pourrait alors
        // administrer l'établissement, et le déblocage exigerait une intervention en base.
        if (request.UserId == actorId)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.UserId), "Vous ne pouvez pas modifier votre propre statut.")
            ]);
        }

        // Lecture via EF : la policy RLS + le Global Query Filter garantissent qu'un Directeur ne peut
        // atteindre QUE les comptes de son école. Cibler l'utilisateur d'une autre école renvoie donc
        // un 404, jamais une modification silencieuse.
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Utilisateur {request.UserId} introuvable.");

        if (user.Status == request.Status)
        {
            // Réécrire le même statut ne changerait rien mais polluerait l'audit d'entrées vides.
            throw new ValidationException([
                new ValidationFailure(nameof(request.Status), $"Le compte est déjà au statut {request.Status}.")
            ]);
        }

        var now = timeProvider.GetUtcNow();
        var previousStatus = user.Status;

        // Changement de statut, écriture de l'audit et révocation des sessions dans UNE transaction :
        // la révocation passe par un UPDATE immédiat (ExecuteUpdate), pas par le change tracker. Hors
        // transaction, un échec du SaveChanges laisserait un compte déconnecté mais toujours actif.
        var revokedSessions = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            user.Status = request.Status;

            dbContext.UserStatusHistory.Add(new UserStatusHistory
            {
                SchoolId = schoolId,
                UserId = user.Id,
                PreviousStatus = previousStatus,
                NewStatus = request.Status,
                Reason = request.Reason,
                ChangedByUserId = actorId,
                ChangedAt = now
            });

            // Réactiver un compte ne coupe évidemment pas ses sessions — il n'en a plus.
            var revoked = request.Status is EntityStatus.Active
                ? 0
                : await authStore.RevokeAllRefreshTokensAsync(user.Id, ct);

            await dbContext.SaveChangesAsync(ct);

            return revoked;
        }, cancellationToken);

        logger.LogInformation(
            "Statut du compte {UserId} : {Previous} -> {New} par {ActorId}. {Revoked} session(s) révoquée(s).",
            user.Id, previousStatus, request.Status, actorId, revokedSessions);

        return new ChangeUserStatusResult(user.Id, previousStatus, request.Status, revokedSessions);
    }
}
