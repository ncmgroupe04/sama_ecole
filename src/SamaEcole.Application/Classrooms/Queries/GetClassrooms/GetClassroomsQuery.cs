using SamaEcole.Domain.Enums;
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
/// <summary>
/// <see cref="Cycle"/> pilote le barème de saisie des notes côté client (Primaire /10, sinon /20) :
/// l'écran de notes adapte l'attribut HTML <c>max</c> et le libellé de colonne selon la classe choisie.
/// Sérialisé en chaîne ("Primaire" / "College" / "Lycee") via le JsonStringEnumConverter global.
/// </summary>
/// <summary>
/// <see cref="IsAccelerated"/> / <see cref="TargetLevel"/> : classe passerelle validant DEUX niveaux
/// (option, voir Classroom.TargetLevel). Faux/null pour l'immense majorité des classes — l'écran Classes
/// n'affiche le badge et le formulaire ne déplie sa liste déroulante que lorsque la case est cochée.
/// </summary>
public record ClassroomDto(
    Guid Id, string Name, string Level, int Capacity, int StudentCount, CycleType Cycle, uint RowVersion,
    bool IsAccelerated = false, string? TargetLevel = null);
