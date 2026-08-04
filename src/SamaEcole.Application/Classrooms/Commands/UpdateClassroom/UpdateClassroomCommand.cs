using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.UpdateClassroom;

/// <summary>
/// PUT /api/v1/classrooms/{id} — corrige le libellé/niveau/capacité d'une classe déjà créée. Le
/// SchoolId n'est pas ici : le Global Query Filter + la RLS bornent déjà la classe visée au tenant
/// courant (AGENTS.md règle #10).
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation (ClassroomDto.RowVersion) :
/// verrouillage optimiste (règle #5), même contrat que UpdateGradeCommand.
/// </summary>
/// <param name="IsAccelerated">Classe passerelle / accélérée (option). Décocher efface le second niveau.</param>
/// <param name="TargetLevel">Second niveau validé, obligatoire quand — et seulement quand — la case est cochée.</param>
public record UpdateClassroomCommand(
    Guid Id, string Name, string Level, int Capacity, uint RowVersion,
    bool IsAccelerated = false, string? TargetLevel = null)
    : IRequest<ClassroomResult>;

/// <summary><see cref="Cycle"/> est recalculé depuis le niveau à chaque correction — voir CreateClassroomResult.</summary>
public record ClassroomResult(
    Guid Id, string Name, string Level, int Capacity, CycleType Cycle, uint RowVersion,
    bool IsAccelerated = false, string? TargetLevel = null);
