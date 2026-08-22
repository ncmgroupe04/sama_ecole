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

/// <summary>
/// <see cref="RowVersion"/> est le jeton xmin nécessaire à UpdateSubjectCommand et
/// DeleteSubjectCommand (AGENTS.md règle #5) — même contrat que GradeCellDto.RowVersion.
///
/// Les champs de STRUCTURE (<see cref="ParentSubjectId"/> … <see cref="Column2Header"/>) décrivent les
/// grilles d'évaluation par compétences du primaire (voir <see cref="SamaEcole.Domain.Entities.Subject"/>).
/// Ils sortent tous à leur valeur neutre pour une matière ordinaire — l'écran des matières les ignore
/// alors entièrement et affiche exactement la liste plate d'avant.
/// </summary>
public record SubjectDto(
    Guid Id,
    string Name,
    string Level,
    decimal Coefficient,
    uint RowVersion,
    Guid? ParentSubjectId = null,
    decimal? MaxScore = null,
    int DisplayOrder = 0,
    string? Column1Header = null,
    string? Column2Header = null);
