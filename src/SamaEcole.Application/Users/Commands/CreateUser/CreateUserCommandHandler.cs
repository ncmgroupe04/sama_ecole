using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;

namespace SamaEcole.Application.Users.Commands.CreateUser;

public class CreateUserCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    ITenantProvider tenantProvider,
    IPasswordHasher passwordHasher)
    : IRequestHandler<CreateUserCommand, CreateUserResult>
{
    public async Task<CreateUserResult> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var email = request.Email.Trim().ToLowerInvariant();

        // L'e-mail identifie un compte sur TOUTE la plateforme, pas seulement l'école courante. Une
        // requête EF classique, sous RLS, ne verrait que les comptes de l'école courante et laisserait
        // passer un doublon d'une AUTRE école jusqu'à la contrainte SQL (409 illisible). IAuthStore
        // passe par la fonction SECURITY DEFINER du chemin de login, qui voit tous les comptes — même
        // mécanisme que la vérification d'unicité de JGK-B01.
        if (await authStore.FindUserByEmailAsync(email, cancellationToken) is not null)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Email), "Un compte utilise déjà cet e-mail.")
            ]);
        }

        var user = new User
        {
            SchoolId = schoolId,
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password),
            FullName = request.FullName.Trim(),
            Role = request.Role,
            Status = EntityStatus.Active
        };

        // INSERT normal (pas de fonction SECURITY DEFINER ici, contrairement à JGK-B01) : l'acteur est
        // un Directeur dont la session porte SON schoolId, la policy RLS "WITH CHECK" est donc
        // naturellement satisfaite. La fonction dédiée n'était nécessaire que pour un Super Admin,
        // dont la session n'a aucun schoolId.
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateUserResult(user.Id, user.FullName, user.Email, user.Role, user.Status);
    }
}
