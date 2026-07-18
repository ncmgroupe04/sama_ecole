using MediatR;

namespace SamaEcole.Application.Students.Commands.CreateStudent;

/// <summary>
/// Pattern de référence pour tout nouveau module (voir ticket JGK-D01, docs/BACKLOG_TICKETS.md).
/// Le matricule n'est PAS fourni ici : il est généré par le Handler, dans la transaction
/// d'écriture, jamais avant (AGENTS.md règle #3).
/// </summary>
public record CreateStudentCommand : IRequest<CreateStudentResult>
{
    public required string FullName { get; init; }
    public required DateOnly BirthDate { get; init; }
    public string? BirthPlace { get; init; }
    public required string Gender { get; init; }
    public required Guid ClassroomId { get; init; }
    public string? PhotoUrl { get; init; }
    public string? GuardianName { get; init; }
    public string? GuardianPhone { get; init; }
}

public record CreateStudentResult(Guid Id, string Matricule);
