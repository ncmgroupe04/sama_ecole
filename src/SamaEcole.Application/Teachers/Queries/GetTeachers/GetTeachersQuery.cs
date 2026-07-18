using MediatR;

namespace SamaEcole.Application.Teachers.Queries.GetTeachers;

/// <summary>
/// GET /api/v1/teachers?page=&amp;pageSize=&amp;search= — ticket JGK-D03 (lecture). Paginée par
/// construction, même raison que GetStudentsQuery (Volume 5 §1).
/// </summary>
public record GetTeachersQuery : IRequest<PaginatedTeachers>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    /// <summary>Recherche sur le nom, le matricule ou l'e-mail.</summary>
    public string? Search { get; init; }
}

public record TeacherListItem(
    Guid Id,
    string Matricule,
    string FullName,
    string Email,
    string? Phone,
    string Status,
    IReadOnlyList<string> Subjects);

public record PaginatedTeachers(IReadOnlyList<TeacherListItem> Items, int TotalCount, int Page, int PageSize);
