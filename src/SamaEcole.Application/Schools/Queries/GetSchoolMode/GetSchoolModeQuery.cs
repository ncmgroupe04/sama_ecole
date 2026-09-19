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

/// <param name="IsLive">Vrai dès que l'établissement est passé en mode réel. Réversible à tout moment.</param>
/// <param name="WentLiveAt">Horodatage du passage COURANT, ou <c>null</c> en mode test.</param>
/// <param name="IsProductionLocked">
/// Verrou PERMANENT (School.IsProductionLocked), posé UNIQUEMENT par une action manuelle et explicite
/// du Directeur (<c>LockProductionCommand</c>) — jamais par le passage en mode réel. L'écran
/// Paramètres s'en sert pour ne jamais proposer « Réinitialiser l'école » dans ce cas — la purge y
/// serait de toute façon refusée (409 RESET_UNAVAILABLE_PRODUCTION_LOCKED), et un bouton qui échoue
/// après une confirmation saisie est précisément la surprise que ce champ évite.
/// </param>
/// <param name="ProductionLockedAt">Horodatage du verrouillage définitif, ou <c>null</c> tant qu'il n'a pas eu lieu.</param>
public sealed record SchoolModeDto(
    bool IsLive,
    DateTimeOffset? WentLiveAt,
    bool IsProductionLocked,
    DateTimeOffset? ProductionLockedAt);

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
            .Select(s => new { s.WentLiveAt, s.IsProductionLocked, s.ProductionLockedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return new SchoolModeDto(
            IsLive: school?.WentLiveAt is not null,
            WentLiveAt: school?.WentLiveAt,
            IsProductionLocked: school?.IsProductionLocked ?? false,
            ProductionLockedAt: school?.ProductionLockedAt);
    }
}
