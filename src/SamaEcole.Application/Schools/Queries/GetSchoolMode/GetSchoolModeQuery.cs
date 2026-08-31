using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Schools.Queries.GetSchoolMode;

/// <summary>
/// GET /schools/current/mode — état « bac à sable / réel » de l'établissement courant, plus le
/// drapeau d'environnement qui pilote le bouton « Repasser en mode test ».
///
/// Lecture LÉGÈRE, ouverte à tout rôle de l'école : la barre supérieure affiche une pastille « Mode
/// test » pour tous tant que l'école n'est pas passée en mode réel, et l'écran Paramètres bascule ses
/// deux régimes de « Zone de danger » là-dessus. Volontairement séparé de <c>SchoolProfileDto</c>
/// (l'identité de l'école) : ce n'est pas une donnée d'identité, et deux consommateurs
/// (topbar + Paramètres) la veulent sans tirer toute la fiche.
///
/// « current » vient du JWT, jamais d'un paramètre (règle #10). <c>schools</c> n'a pas de Global
/// Query Filter tenant : on filtre EXPLICITEMENT sur l'Id du tenant courant.
/// </summary>
public record GetSchoolModeQuery : IRequest<SchoolModeDto>;

/// <param name="IsLive">Vrai dès que l'établissement est passé en mode réel.</param>
/// <param name="WentLiveAt">Horodatage du passage, ou <c>null</c> en mode test.</param>
/// <param name="RevertToTestAvailable">
/// Vrai si l'environnement autorise le retour en mode test (Development ou
/// <c>SAMA_RETOUR_MODE_TEST_AUTORISE=true</c>). Toujours faux en vraie production.
/// </param>
public sealed record SchoolModeDto(bool IsLive, DateTimeOffset? WentLiveAt, bool RevertToTestAvailable);

public class GetSchoolModeQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISandboxModeProvider sandboxMode)
    : IRequestHandler<GetSchoolModeQuery, SchoolModeDto>
{
    public async Task<SchoolModeDto> Handle(GetSchoolModeQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var wentLiveAt = await dbContext.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.WentLiveAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new SchoolModeDto(
            IsLive: wentLiveAt is not null,
            WentLiveAt: wentLiveAt,
            RevertToTestAvailable: sandboxMode.RevertToTestEnabled);
    }
}
