using MediatR;

namespace SamaEcole.Application.Students.Queries.GetStudents;

/// <summary>
/// GET /api/v1/students?page=&amp;pageSize=&amp;search= — ticket JGK-D01 (lecture).
///
/// Paginée par construction : une école secondaire sénégalaise compte couramment plus de mille
/// élèves, et une liste non bornée finirait par saturer une connexion mobile (Volume 5 §1).
/// </summary>
public record GetStudentsQuery : IRequest<PaginatedStudents>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>Recherche sur le nom ou le matricule (Volume 5 §1 : « recherche sur toutes les listes »).</summary>
    public string? Search { get; init; }

    /// <summary>Filtre optionnel sur une classe.</summary>
    public Guid? ClassroomId { get; init; }
}

public record StudentListItem(
    Guid Id,
    string Matricule,
    string FullName,
    DateOnly BirthDate,
    string Gender,
    Guid ClassroomId,
    string ClassroomName,
    string? GuardianName,
    string? GuardianPhone);

public record PaginatedStudents(IReadOnlyList<StudentListItem> Items, int TotalCount, int Page, int PageSize);
