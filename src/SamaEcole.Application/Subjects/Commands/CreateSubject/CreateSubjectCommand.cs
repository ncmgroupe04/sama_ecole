using MediatR;

namespace SamaEcole.Application.Subjects.Commands.CreateSubject;

/// <summary>
/// POST /api/v1/subjects — openapi.yaml §SubjectCreateRequest, ticket JGK-C03.
/// Le SchoolId n'est PAS ici : il est lu dans le JWT via ITenantProvider, jamais accepté du client
/// (AGENTS.md règle #10) — sans quoi n'importe qui créerait une matière dans l'école d'un autre.
/// </summary>
public record CreateSubjectCommand : IRequest<SubjectResult>
{
    public required string Name { get; init; }
    public required string Level { get; init; }
    public decimal Coefficient { get; init; }
}

public record SubjectResult(Guid Id, string Name, string Level, decimal Coefficient);
