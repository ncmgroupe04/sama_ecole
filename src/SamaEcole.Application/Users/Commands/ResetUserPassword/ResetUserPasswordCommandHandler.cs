using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Users.Commands.ResetUserPassword;

public class ResetUserPasswordCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    IPasswordHasher passwordHasher,
    ILogger<ResetUserPasswordCommandHandler> logger)
    : IRequestHandler<ResetUserPasswordCommand, ResetUserPasswordResult>
{
    public async Task<ResetUserPasswordResult> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Même garde que ChangeUserStatus (JGK-A05) : cette voie administrative sert à gérer AUTRUI,
        // pas à changer son propre mot de passe.
        if (request.UserId == actorId)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.UserId), "Vous ne pouvez pas réinitialiser votre propre mot de passe ici.")
            ]);
        }

        // Policy RLS + Global Query Filter : un Directeur ne peut atteindre QUE les comptes de son
        // école. Cibler l'utilisateur d'une autre école renvoie un 404, jamais une modification silencieuse.
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Utilisateur {request.UserId} introuvable.");

        var revokedSessions = await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            user.PasswordHash = passwordHasher.Hash(request.NewPassword);

            // Un mot de passe qu'on vient de changer ne doit pas laisser une session déjà ouverte
            // utilisable sous l'ancien — même raisonnement que le blocage (JGK-A05).
            var revoked = await authStore.RevokeAllRefreshTokensAsync(user.Id, ct);

            await dbContext.SaveChangesAsync(ct);

            return revoked;
        }, cancellationToken);

        logger.LogInformation(
            "Mot de passe réinitialisé pour le compte {UserId} par {ActorId}. {Revoked} session(s) révoquée(s).",
            user.Id, actorId, revokedSessions);

        return new ResetUserPasswordResult(user.Id, revokedSessions);
    }
}
