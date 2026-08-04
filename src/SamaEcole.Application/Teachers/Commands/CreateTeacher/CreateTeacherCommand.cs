using MediatR;

namespace SamaEcole.Application.Teachers.Commands.CreateTeacher;

/// <summary>
/// POST /api/v1/teachers — openapi.yaml §TeacherCreateRequest, ticket JGK-D03. Le matricule n'est
/// PAS ici : il est généré par le Handler, dans la transaction d'écriture, jamais avant (AGENTS.md
/// règle #3, même contrat que CreateStudentCommand).
/// </summary>
public record CreateTeacherCommand : IRequest<CreateTeacherResult>
{
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }

    /// <summary>Obligatoire à la création — voir <see cref="Domain.Entities.Teacher.BirthDate"/>.</summary>
    public required DateOnly BirthDate { get; init; }

    public string? BirthPlace { get; init; }

    /// <summary>Adresse de résidence (facultative) — voir <see cref="Domain.Entities.Teacher.Address"/>.</summary>
    public string? Address { get; init; }

    public string? PhotoUrl { get; init; }

    /// <summary>Photo téléversée (feature B), déjà compressée côté client, en base64 — voir Teacher.PhotoData.</summary>
    public string? PhotoData { get; init; }

    /// <summary>Matières que l'enseignant est qualifié à enseigner (openapi.yaml : requis, au moins une).</summary>
    public required IReadOnlyList<Guid> SubjectIds { get; init; }

    /// <summary>
    /// Compte de connexion (rôle Enseignant) à rattacher à la fiche (ticket JGK-D06). Optionnel : une
    /// fiche RH peut être créée sans compte. S'il est fourni, il DOIT désigner un utilisateur Enseignant
    /// de la même école, non encore rattaché à une autre fiche — sinon la saisie de l'appel ne pourrait
    /// pas remonter aux classes assignées de cet enseignant.
    /// </summary>
    public Guid? UserId { get; init; }
}

public record CreateTeacherResult(Guid Id, string Matricule);
