using MediatR;

namespace SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;

/// <summary>
/// POST /api/v1/school-years — openapi.yaml §SchoolYearCreateRequest, ticket JGK-C01.
///
/// Le SchoolId n'est PAS ici : il est lu dans le JWT via ITenantProvider, jamais accepté du client
/// (AGENTS.md règle #10) — sans quoi n'importe qui créerait une année dans l'école d'un autre.
///
/// Pas de champ « isActive » non plus, et c'est délibéré : créer une année ne doit JAMAIS faire
/// basculer l'établissement dessus. Une école qui prépare « 2027-2028 » au mois d'août travaille
/// encore sur « 2026-2027 » — un basculement en effet de bord rattacherait les inscriptions du jour
/// au mauvais exercice. Le basculement est une action explicite : POST /school-years/{id}/activate.
///
/// Seule exception, dans le Handler : la toute PREMIÈRE année d'une école devient active d'office —
/// une école sans année active ne peut rien faire, et exiger un second appel n'y ajouterait aucune
/// sécurité.
/// </summary>
public record CreateSchoolYearCommand : IRequest<SchoolYearDto>
{
    /// <summary>Ex. « 2026-2027 ».</summary>
    public required string Label { get; init; }

    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
}
