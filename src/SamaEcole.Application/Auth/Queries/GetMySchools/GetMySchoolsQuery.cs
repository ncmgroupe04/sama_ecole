using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Auth.Queries.GetMySchools;

/// <summary>
/// GET /auth/my-schools — établissements entre lesquels l'utilisateur courant peut basculer
/// (groupe scolaire). Alimente le sélecteur de la barre supérieure.
///
/// Renvoie une liste VIDE pour un utilisateur mono-école : le sélecteur ne s'affiche alors pas, et
/// rien ne change pour l'immense majorité des comptes. C'est ce qui rend la fonctionnalité
/// strictement additive.
///
/// L'école d'origine (<c>User.SchoolId</c>) est incluse dans la liste : sans elle, un promoteur
/// ayant basculé vers une autre école n'aurait aucun moyen de revenir à la sienne.
/// </summary>
public record GetMySchoolsQuery : IRequest<IReadOnlyList<SwitchableSchoolDto>>;

public record SwitchableSchoolDto(Guid SchoolId, string Name, bool IsCurrent);

public class GetMySchoolsQueryHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetMySchoolsQuery, IReadOnlyList<SwitchableSchoolDto>>
{
    public async Task<IReadOnlyList<SwitchableSchoolDto>> Handle(
        GetMySchoolsQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        // Filtre sur UserId, issu du claim `sub` — jamais d'un paramètre client (AGENTS.md règle #10).
        // `user_schools` est hors RLS par nécessité (voir l'entité) : c'est CE filtre qui borne la
        // lecture aux rattachements de l'appelant, il ne doit jamais être relâché.
        var attachedSchoolIds = await dbContext.UserSchools
            .AsNoTracking()
            .Where(us => us.UserId == userId)
            .Select(us => us.SchoolId)
            .ToListAsync(cancellationToken);

        // Aucun rattachement supplémentaire : compte mono-école, rien à proposer. On sort AVANT toute
        // autre requête — ce chemin est celui de presque tous les utilisateurs.
        if (attachedSchoolIds.Count == 0)
        {
            return [];
        }

        var currentSchoolId = tenantProvider.CurrentSchoolId;

        if (currentSchoolId is { } current && !attachedSchoolIds.Contains(current))
        {
            attachedSchoolIds.Add(current);
        }

        // `schools` n'est pas une table tenant : lecture directe, filtrée par la liste ci-dessus.
        // Les établissements suspendus ou bloqués sont écartés — proposer une bascule vers une école
        // dont l'accès est coupé ne mènerait qu'à une session inutilisable.
        return await dbContext.Schools
            .AsNoTracking()
            .Where(s => attachedSchoolIds.Contains(s.Id) && s.Status == EntityStatus.Active)
            .OrderBy(s => s.Name)
            .Select(s => new SwitchableSchoolDto(s.Id, s.Name, s.Id == currentSchoolId))
            .ToListAsync(cancellationToken);
    }
}
