using MediatR;
using SamaEcole.Application.Subscriptions;

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

    /// <summary>Lieu de naissance — obligatoire (feature E) : voir <see cref="Domain.Entities.Student.BirthPlace"/>.</summary>
    public required string BirthPlace { get; init; }

    public required string Gender { get; init; }
    public required Guid ClassroomId { get; init; }
    public string? PhotoUrl { get; init; }

    /// <summary>Photo téléversée (feature B), déjà compressée côté client, en base64 — voir Student.PhotoData.</summary>
    public string? PhotoData { get; init; }

    public string? GuardianName { get; init; }
    public string? GuardianPhone { get; init; }
    public string? GuardianEmail { get; init; }
    public string? Address { get; init; }
    public string? FullNameAr { get; init; }
    public string? GuardianNameAr { get; init; }
}

/// <param name="QuotaWarning">Non nul quand l'effectif dépasse désormais le plafond nominal (tolérance entamée).</param>
public record CreateStudentResult(Guid Id, string Matricule, StudentQuotaWarning? QuotaWarning = null);
