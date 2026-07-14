using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.CreateClassroom;

/// <summary>
/// POST /api/v1/classrooms — openapi.yaml, ticket JGK-C02.
/// Le SchoolId n'est PAS ici : il est lu dans le JWT via ITenantProvider, jamais accepté du client
/// (AGENTS.md règle #10) — sans quoi n'importe qui créerait une classe dans l'école d'un autre.
/// </summary>
public record CreateClassroomCommand : IRequest<CreateClassroomResult>
{
    public required string Name { get; init; }
    public required string Level { get; init; }
    public int Capacity { get; init; }
}

public record CreateClassroomResult(Guid Id, string Name, string Level, int Capacity);
