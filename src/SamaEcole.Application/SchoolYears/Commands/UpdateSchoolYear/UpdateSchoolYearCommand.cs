using MediatR;

namespace SamaEcole.Application.SchoolYears.Commands.UpdateSchoolYear;

/// <summary>
/// PUT /api/v1/school-years/{id} — corrige le libellé et/ou la période d'une année scolaire
/// (ticket JGK-C01, extension : « prolonger une année en cas de décalage du calendrier »).
///
/// Le SchoolId n'est PAS ici : il est lu dans le JWT via ITenantProvider (AGENTS.md règle #10). L'id
/// vient de la ROUTE, jamais du corps — il ne doit pas pouvoir diverger de la ressource visée.
///
/// Pas de champ « isActive » : modifier des dates ne fait JAMAIS basculer l'établissement sur cette
/// année. Le basculement reste une action explicite et confirmée par mot de passe
/// (POST /school-years/{id}/activate, docs/Volume_7_Security.md §16).
///
/// Changer la période RECALE les trimestres de l'année (voir UpdateSchoolYearCommandHandler) : ils en
/// sont déduits, les laisser en place produirait des bulletins datés hors de leur propre année.
/// </summary>
public record UpdateSchoolYearCommand : IRequest<SchoolYearDto>
{
    public Guid Id { get; init; }

    /// <summary>Ex. « 2026-2027 ».</summary>
    public required string Label { get; init; }

    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
}
