using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Queries.ListAssignableStudents;

/// <summary>
/// GET /api/v1/internat/assignable-students — les élèves que la Direction peut rattacher à la Halqa d'un Oustaz, avec
/// l'Oustaz actuel de chacun (<c>null</c> = sans Halqa). Alimente l'écran d'affectation de /oustaz ; l'écriture, elle,
/// reste <c>POST students/assign-instructor</c>.
///
/// <see cref="Scope"/> : <c>unassigned</c> (défaut) ne garde que les élèves sans Oustaz — le cas courant, ceux qui n'ont
/// aucun accès au bulletin coranique — ; <c>all</c> y ajoute ceux déjà rattachés, pour les changer de Halqa (le client
/// prévient alors que l'élève sera déplacé). Le périmètre est celui du tableau de bord du suivi coranique : tous les élèves
/// de l'école, pas seulement ceux inscrits à l'année active (une Halqa ne dépend pas de l'année scolaire).
///
/// <see cref="Search"/> (nom français, nom arabe ou matricule) ne filtre qu'à partir de deux caractères : un seul est trop
/// large pour être utile. Pagination à la convention du Volume 4 §0.2 (20 par page, 100 au plus).
/// </summary>
public record ListAssignableStudentsQuery : IRequest<PaginatedAssignableStudents>
{
    public string? Search { get; init; }
    public string Scope { get; init; } = ScopeUnassigned;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public const string ScopeUnassigned = "unassigned";
    public const string ScopeAll = "all";
}

/// <param name="InstructorId">Oustaz actuel de l'élève, ou <c>null</c> s'il n'a pas de Halqa.</param>
/// <param name="InstructorName">Nom de cet Oustaz (pour prévenir la Direction qu'affecter l'élève le déplacera).</param>
public record AssignableStudentDto(
    Guid StudentId,
    string Matricule,
    string FullName,
    string? FullNameAr,
    string ClassroomName,
    Guid? InstructorId,
    string? InstructorName);

public record PaginatedAssignableStudents(IReadOnlyList<AssignableStudentDto> Items, int TotalCount, int Page, int PageSize);

public class ListAssignableStudentsQueryValidator : AbstractValidator<ListAssignableStudentsQuery>
{
    private static readonly string[] Scopes = [ListAssignableStudentsQuery.ScopeUnassigned, ListAssignableStudentsQuery.ScopeAll];

    public ListAssignableStudentsQueryValidator()
    {
        RuleFor(x => x.Scope)
            .Must(s => Scopes.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Périmètre inconnu : choisissez unassigned ou all.");
        RuleFor(x => x.Search).MaximumLength(100).NoHtml();
    }
}

public class ListAssignableStudentsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ListAssignableStudentsQuery, PaginatedAssignableStudents>
{
    private const int MinTermLength = 2;
    private const int MaxPageSize = 100;

    public async Task<PaginatedAssignableStudents> Handle(
        ListAssignableStudentsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        // Global Query Filter + RLS : seuls les élèves vivants de l'école courante sont visibles.
        var students = dbContext.Students.AsNoTracking();

        if (string.Equals(request.Scope, ListAssignableStudentsQuery.ScopeUnassigned, StringComparison.OrdinalIgnoreCase))
        {
            students = students.Where(s => s.InstructorId == null);
        }

        // ToLower() plutôt qu'EF.Functions.ILike : Application reste ignorante du provider (même choix que ListBoardersQuery).
        var term = request.Search?.Trim().ToLower();
        if (term is { Length: >= MinTermLength })
        {
            students = students.Where(s =>
                s.FullName.ToLower().Contains(term)
                || s.Matricule.ToLower().Contains(term)
                || (s.FullNameAr != null && s.FullNameAr.ToLower().Contains(term)));
        }

        var total = await students.CountAsync(cancellationToken);

        var items = await students
            .OrderBy(s => s.FullName).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new AssignableStudentDto(
                s.Id,
                s.Matricule,
                s.FullName,
                s.FullNameAr,
                // Une classe supprimée sort du filtre : sans IgnoreQueryFilters, l'élève perdrait son nom de classe (voire
                // disparaîtrait de la liste). IgnoreQueryFilters lève AUSSI le filtre tenant : on réimpose SchoolId (la RLS reste
                // la seconde barrière). Le repli ne sert que si la ligne manque tout à fait.
                dbContext.Classrooms.IgnoreQueryFilters()
                    .Where(c => c.Id == s.ClassroomId && c.SchoolId == s.SchoolId)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? "Classe introuvable",
                s.InstructorId,
                dbContext.Instructors
                    .Where(i => i.Id == s.InstructorId)
                    .Select(i => i.FullName)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new PaginatedAssignableStudents(items, total, page, pageSize);
    }
}
