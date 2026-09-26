using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Users.Commands.UpdateUserProfile;

public class UpdateUserProfileCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser)
    : IRequestHandler<UpdateUserProfileCommand, UpdateUserProfileResult>
{
    public async Task<UpdateUserProfileResult> Handle(UpdateUserProfileCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var isSelf = request.UserId == actorId;

        // Lecture via EF : la policy RLS + le Global Query Filter garantissent qu'un Directeur ne peut
        // atteindre QUE les comptes de son école. Cibler l'utilisateur d'une autre école renvoie donc
        // un 404, jamais une modification silencieuse — même garanti que ChangeUserStatusCommandHandler.
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.SchoolId == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Utilisateur {request.UserId} introuvable.");

        var email = EmailNormalizer.Normalize(request.Email);

        // Sur SA PROPRE fiche, le nom est librement corrigeable, mais l'e-mail ne l'est pas ici : il
        // sert à se connecter et à récupérer le compte, donc son changement passe par la voie sécurisée
        // /auth/change-email (mot de passe actuel requis, sessions révoquées) — sinon une session volée
        // suffirait à détourner le compte. Renvoyer l'e-mail inchangé (ce que fait la modale) reste accepté.
        // Ce Handler ne reçoit ni rôle ni statut : rien d'autre à protéger contre l'auto-dégradation
        // (blocage/suspension de soi-même : ChangeUserStatusCommandHandler).
        if (isSelf && !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Email), "Pour changer votre propre e-mail, utilisez « Changer mon e-mail » (mot de passe requis).")
            ]);
        }

        // Même garde d'unicité GLOBALE (toute la plateforme, pas seulement l'école courante) que
        // CreateUserCommandHandler — en excluant le compte qu'on modifie lui-même, sinon renvoyer le
        // même e-mail inchangé se rejetterait comme un doublon de soi-même.
        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await authStore.FindUserByEmailAsync(email, cancellationToken);
            if (existing is not null && existing.Id != user.Id)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.Email), "Un compte utilise déjà cet e-mail.")
                ]);
            }
        }

        user.FullName = request.FullName.Trim();
        user.Email = email;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateUserProfileResult(user.Id, user.FullName, user.Email);
    }
}
