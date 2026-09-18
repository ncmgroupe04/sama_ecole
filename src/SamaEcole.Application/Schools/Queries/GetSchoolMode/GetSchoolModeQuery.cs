using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Schools.Queries.GetSchoolMode;

/// <summary>
/// GET /schools/current/mode — état « bac à sable / réel » de l'établissement courant.
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
/// <param name="HasEverGoneLive">
/// Verrou PERMANENT (School.HasEverGoneLive) : vrai si l'établissement est DÉJÀ passé en mode réel
/// une fois, même s'il est repassé en mode test depuis (<c>IsLive</c> serait alors faux). L'écran
/// Paramètres s'en sert pour ne jamais proposer « Réinitialiser l'école » dans ce cas — la purge y
/// serait de toute façon refusée (409 RESET_UNAVAILABLE_LIVE_MODE), et un bouton qui échoue après
/// une confirmation saisie est précisément la surprise que ce champ évite.
/// </param>
public sealed record SchoolModeDto(bool IsLive, DateTimeOffset? WentLiveAt, bool HasEverGoneLive);

public class GetSchoolModeQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetSchoolModeQuery, SchoolModeDto>
{
    public async Task<SchoolModeDto> Handle(GetSchoolModeQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var school = await dbContext.Schools
            .AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new { s.WentLiveAt, s.HasEverGoneLive })
            .FirstOrDefaultAsync(cancellationToken);

        return new SchoolModeDto(
            IsLive: school?.WentLiveAt is not null,
            WentLiveAt: school?.WentLiveAt,
            HasEverGoneLive: school?.HasEverGoneLive ?? false);
    }
}
