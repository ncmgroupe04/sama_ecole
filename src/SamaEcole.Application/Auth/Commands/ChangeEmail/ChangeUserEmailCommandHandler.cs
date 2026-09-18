using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Auth.Commands.ChangeEmail;

/// <summary>
/// Vérifie le mot de passe actuel puis applique le nouvel e-mail. Passe entièrement par IAuthStore,
/// jamais par IApplicationDbContext : `users` est sous RLS (ticket JGK-A03), et un Super Admin — qui
/// change aussi son propre e-mail par cette route — n'a aucun SchoolId de session pour satisfaire la
/// policy. FindUserByIdAsync et ChangeEmailAsync passent tous deux par les fonctions SECURITY DEFINER
/// du chemin de login, qui fonctionnent identiquement pour un Directeur et pour un Super Admin — même
/// raisonnement, mêmes garanties que ChangePasswordCommandHandler.
///
/// Toutes les sessions sont révoquées (voir AuthStore.ChangeEmailAsync) : un compte dont l'identifiant
/// de connexion vient de changer ne doit pas laisser une session déjà ouverte utilisable — y compris
/// celle qui vient de faire la demande, qui devra se reconnecter avec la nouvelle adresse.
/// </summary>
public class ChangeUserEmailCommandHandler(
    ICurrentUserService currentUser,
    IAuthStore authStore,
    IPasswordHasher passwordHasher,
    ILogger<ChangeUserEmailCommandHandler> logger)
    : IRequestHandler<ChangeUserEmailCommand, ChangeUserEmailResult>
{
    public async Task<ChangeUserEmailResult> Handle(ChangeUserEmailCommand request, CancellationToken cancellationToken)
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

        var newEmail = EmailNormalizer.Normalize(request.NewEmail);

        // Pré-contrôle applicatif : message d'erreur plus rapide et plus clair dans le cas courant
        // (quelqu'un saisit par erreur l'e-mail d'un autre compte). Ne remplace PAS le filet de
        // sécurité en base (SetEmailAsync, AuthStore) : deux requêtes concurrentes visant le même
        // e-mail franchiraient toutes deux ce contrôle avant que l'une ne commite — seul l'index
        // unique citext de `users.Email` tranche réellement la course.
        //
        // DuplicateRecordException (409), pas ValidationException (422) : un e-mail déjà pris est un
        // conflit refusé par la base (même famille que le filet de sécurité ci-dessous), pas une
        // saisie invalide — les deux chemins doivent renvoyer le même statut pour la même situation.
        if (!string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase)
            && await authStore.FindUserByEmailAsync(newEmail, cancellationToken) is not null)
        {
            throw new DuplicateRecordException("Un compte utilise déjà cet e-mail.", "users / IX_users_Email");
        }

        var revokedSessions = await authStore.ChangeEmailAsync(userId, newEmail, cancellationToken);

        logger.LogInformation(
            "E-mail de connexion changé en libre-service par {UserId}. {Revoked} session(s) révoquée(s).",
            userId, revokedSessions);

        return new ChangeUserEmailResult(newEmail, revokedSessions);
    }
}
