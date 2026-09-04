using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.ChangePassword;

/// <summary>
/// Vérifie l'ancien mot de passe puis applique le nouveau. Passe entièrement par IAuthStore, jamais
/// par IApplicationDbContext : `users` est sous RLS (ticket JGK-A03), et un Super Admin — qui change
/// aussi son propre mot de passe par cette route — n'a aucun SchoolId de session pour satisfaire la
/// policy. FindUserByIdAsync et ChangePasswordAsync passent tous deux par les fonctions SECURITY
/// DEFINER du chemin de login, qui fonctionnent identiquement pour un Directeur et pour un Super Admin.
///
/// Toutes les sessions sont révoquées, comme les deux autres voies de changement de mot de passe
/// (ResetPasswordCommandHandler, ResetUserPasswordCommandHandler) : un mot de passe qu'on vient de
/// changer ne doit pas laisser une session déjà ouverte utilisable sous l'ancien — y compris celle qui
/// vient de faire la demande, qui devra se reconnecter.
/// </summary>
public class ChangePasswordCommandHandler(
    ICurrentUserService currentUser,
    IAuthStore authStore,
    IPasswordHasher passwordHasher,
    ILogger<ChangePasswordCommandHandler> logger)
    : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
{
    public async Task<ChangePasswordResult> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var user = await authStore.FindUserByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Compte introuvable.");

        if (!passwordHasher.Verify(user.PasswordHash, request.CurrentPassword))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.CurrentPassword), "Mot de passe actuel incorrect.")
            ]);
        }

        var revokedSessions = await authStore.ChangePasswordAsync(
            userId, passwordHasher.Hash(request.NewPassword), cancellationToken);

        logger.LogInformation(
            "Mot de passe changé en libre-service par {UserId}. {Revoked} session(s) révoquée(s).",
            userId, revokedSessions);

        return new ChangePasswordResult(revokedSessions);
    }
}
