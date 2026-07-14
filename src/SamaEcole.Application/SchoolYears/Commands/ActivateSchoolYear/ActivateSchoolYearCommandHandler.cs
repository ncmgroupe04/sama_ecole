using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.SchoolYears.Commands.ActivateSchoolYear;

public class ActivateSchoolYearCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    ILogger<ActivateSchoolYearCommandHandler> logger)
    : IRequestHandler<ActivateSchoolYearCommand, SchoolYearDto>
{
    public async Task<SchoolYearDto> Handle(ActivateSchoolYearCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        await ConfirmPasswordAsync(actorId, request.Password, cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser
        // l'année d'une autre école renvoie 404, jamais une activation silencieuse chez le voisin.
        var target = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {request.Id} introuvable.");

        // Déjà active : rien à faire. La désactiver pour la réactiver la ferait buter sur l'index
        // unique partiel contre elle-même, pour un résultat identique.
        if (target.IsActive)
        {
            return ToDto(target, today);
        }

        // Années passées en LECTURE SEULE (ticket JGK-C01) : une année terminée ne peut pas redevenir
        // l'exercice courant. Sans cette garde, les inscriptions du jour s'imputeraient sur un
        // exercice clos, dont les frais et les bulletins sont déjà arrêtés.
        //
        // Une année à VENIR reste activable : préparer les réinscriptions quelques semaines avant la
        // rentrée est un usage normal, pas une erreur.
        if (target.IsClosedOn(today))
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.Id),
                    $"L'année scolaire « {target.Label} » est terminée : les années passées sont en lecture seule.")
            ]);
        }

        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var current = await dbContext.SchoolYears.FirstOrDefaultAsync(y => y.IsActive, ct);

            // DEUX SaveChanges, dans UNE transaction, et dans CET ordre.
            //
            // L'index unique partiel « une seule année active par école » est vérifié par PostgreSQL à
            // chaque instruction, sans report possible — un index partiel ne peut pas être DEFERRABLE.
            // Or EF Core ne garantit pas l'ordre des UPDATE au sein d'un même lot : si l'activation
            // partait avant la désactivation, deux lignes seraient actives l'espace d'une instruction
            // et PostgreSQL refuserait l'écriture.
            //
            // On désactive donc d'abord, on active ensuite. La transaction garantit qu'aucun autre
            // client ne verra jamais l'école privée d'année active entre les deux.
            if (current is not null)
            {
                current.IsActive = false;
                await dbContext.SaveChangesAsync(ct);
            }

            target.IsActive = true;
            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation(
                "Année scolaire active de l'école {SchoolId} : {Previous} -> {New}, par {ActorId}.",
                schoolId, current?.Label ?? "aucune", target.Label, actorId);

            return target.Id;
        }, cancellationToken);

        return ToDto(target, today);
    }

    /// <summary>
    /// Double confirmation (docs/Volume_7_Security.md §16). Le mot de passe est comparé au hash du
    /// compte courant : c'est bien la personne devant l'écran qu'on vérifie, pas la validité du
    /// jeton — celle-là est déjà acquise.
    /// </summary>
    private async Task ConfirmPasswordAsync(Guid actorId, string password, CancellationToken cancellationToken)
    {
        var passwordHash = await dbContext.Users
            .Where(u => u.Id == actorId)
            .Select(u => u.PasswordHash)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("Utilisateur courant introuvable.");

        if (passwordHasher.Verify(passwordHash, password))
        {
            return;
        }

        // Événement de sécurité : on tente de faire basculer l'école sans en connaître le mot de passe
        // — poste laissé ouvert, jeton dérobé. Tracé, sans jamais journaliser la saisie elle-même.
        logger.LogWarning(
            "Confirmation par mot de passe REFUSÉE pour un changement d'année scolaire. Utilisateur {ActorId}.",
            actorId);

        // 422 sur le champ, et non 401 : la session est parfaitement valide, c'est la CONFIRMATION qui
        // échoue. Un 401 ferait croire au client que son jeton a expiré et le déconnecterait, là où
        // l'utilisateur doit simplement resaisir son mot de passe sous le champ concerné.
        throw new ValidationException([
            new ValidationFailure(nameof(ActivateSchoolYearCommand.Password), "Mot de passe incorrect.")
        ]);
    }

    private static SchoolYearDto ToDto(SchoolYear year, DateOnly today) =>
        new(year.Id, year.Label, year.StartDate, year.EndDate, year.IsActive, year.IsClosedOn(today));
}
