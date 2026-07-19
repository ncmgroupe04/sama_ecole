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
public record UpdateClassroomCommand(Guid Id, string Name, string Level, int Capacity, uint RowVersion)
    : IRequest<ClassroomResult>;

public record ClassroomResult(Guid Id, string Name, string Level, int Capacity, uint RowVersion);
