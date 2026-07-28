using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Auth.Commands.Logout;

/// <summary>
/// Ticket JGK-A04 — révocation des refresh tokens à la déconnexion.
///
/// L'API étant sans état (docs/Volume_4_API_Design.md §1.1), rien ne permet d'identifier « le »
/// refresh token de l'appelant à partir de son seul access token : on révoque donc TOUS ses refresh
/// tokens actifs. C'est aussi le comportement le plus sûr — un logout sur un poste partagé ne laisse
/// aucune session ouverte ailleurs.
///
/// L'access token déjà émis, lui, reste valide jusqu'à son expiration (15 min) : c'est le prix de
/// l'absence d'état côté serveur, assumé par le Volume 4.
/// </summary>
public class LogoutCommandHandler(IAuthStore authStore, ICurrentUserService currentUser)
    : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        await authStore.RevokeAllRefreshTokensAsync(userId, cancellationToken);
    }
}