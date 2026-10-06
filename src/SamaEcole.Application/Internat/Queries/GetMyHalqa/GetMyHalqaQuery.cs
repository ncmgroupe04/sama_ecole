using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat.Queries.GetInstructorStudents;
using MediatR;

namespace SamaEcole.Application.Internat.Queries.GetMyHalqa;

/// <summary>
/// GET /api/v1/internat/my-halqa — la Halqa de l'Oustaz CONNECTÉ, résolue depuis son compte. C'est l'entrée de la
/// tablette : le JWT ne porte que l'identifiant du compte, jamais celui de la fiche d'Oustaz, donc le client ne peut
/// pas appeler GET instructors/{id}/students sans ce détour. Aucun identifiant n'est accepté en paramètre : on ne peut
/// pas viser la Halqa d'un autre.
///
/// Réservé au rôle Enseignant côté contrôleur. Un compte sans fiche d'Oustaz, ou dont la fiche est suspendue, reçoit
/// le même 403 actionnable que partout ailleurs (HalqaScopeAuthorizer).
/// </summary>
public record GetMyHalqaQuery : IRequest<HalqaDto>;

public class GetMyHalqaQueryHandler(IApplicationDbContext dbContext, HalqaScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<GetMyHalqaQuery, HalqaDto>
{
    public async Task<HalqaDto> Handle(GetMyHalqaQuery request, CancellationToken cancellationToken)
    {
        // null = rôle non borné (Directeur, Secrétariat…) : ces rôles n'ont pas de Halqa « à eux ». Le contrôleur les
        // écarte déjà ; cette garde protège l'appel direct du Handler.
        var own = await scopeAuthorizer.GetOwnInstructorIdOrNullAsync(cancellationToken)
            ?? throw new ForbiddenException("Cet écran est réservé aux Oustaz : utilisez la liste des Oustaz de l'Internat.");

        return await HalqaReader.LoadAsync(dbContext, own, cancellationToken);
    }
}
