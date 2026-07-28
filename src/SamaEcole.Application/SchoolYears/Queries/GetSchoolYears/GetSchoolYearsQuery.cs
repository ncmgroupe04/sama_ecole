using MediatR;

namespace SamaEcole.Application.SchoolYears.Queries.GetSchoolYears;

/// <summary>
/// GET /api/v1/school-years — ticket JGK-C01.
///
/// Aucun paramètre : les années de l'école courante, et elles seules. Le tenant vient du JWT, et le
/// Global Query Filter + la policy RLS s'en chargent — cette requête ne filtre RIEN à la main sur
/// SchoolId, précisément pour qu'un oubli soit impossible.
///
/// Query et non Command : lecture seule (AGENTS.md règle #7, CQRS).
/// </summary>
public record GetSchoolYearsQuery : IRequest<IReadOnlyList<SchoolYearDto>>;
