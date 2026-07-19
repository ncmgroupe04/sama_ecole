using MediatR;

namespace SamaEcole.Application.Classrooms.Queries.GetClassrooms;

/// <summary>
/// GET /api/v1/classrooms — ticket JGK-C02.
///
/// Aucun paramètre : les classes de l'école courante, et elles seules. Le tenant vient du JWT, et
/// le Global Query Filter + la policy RLS s'en chargent — cette requête ne filtre RIEN à la main
/// sur SchoolId, précisément pour qu'un oubli soit impossible.
///
/// Query et non Command : lecture seule, aucune écriture (AGENTS.md règle #7, CQRS).
/// </summary>
public record GetClassroomsQuery : IRequest<IReadOnlyList<ClassroomDto>>;

/// <summary>
/// Le décompte d'élèves est calculé côté base : la liste sert à remplir un sélecteur autant qu'un
/// tableau. <see cref="RowVersion"/> est le jeton xmin nécessaire à UpdateClassroomCommand et
/// DeleteClassroomCommand (AGENTS.md règle #5) — même contrat que GradeCellDto.RowVersion.
/// </summary>
public record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount, uint RowVersion);
