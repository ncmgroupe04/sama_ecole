using MediatR;

namespace SamaEcole.Application.Subjects.Queries.GetSubjects;

/// <summary>
/// GET /api/v1/subjects — ticket JGK-C03.
///
/// Aucun paramètre : les matières de l'école courante, et elles seules. Le tenant vient du JWT, et le
/// Global Query Filter + la policy RLS s'en chargent — cette requête ne filtre RIEN à la main sur
/// SchoolId, précisément pour qu'un oubli soit impossible.
///
/// Query et non Command : lecture seule (AGENTS.md règle #7, CQRS).
/// </summary>
public record GetSubjectsQuery : IRequest<IReadOnlyList<SubjectDto>>;

public record SubjectDto(Guid Id, string Name, string Level, decimal Coefficient);
