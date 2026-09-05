using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.SearchStudentsForCashier;

/// <summary>
/// GET /finance/students/search — recherche élève pour la caisse (ticket écran Caisse). Reprend le
/// filtre nom/matricule de GetStudentsQuery, mais y ajoute directement le solde de l'inscription
/// active : la caissière doit voir, DANS LA LISTE de résultats, qu'un élève vient d'être inscrit par
/// le secrétariat mais n'a encore rien versé — sans avoir à sélectionner chaque élève un par un pour
/// le découvrir. Un élève venant d'être inscrit (AmountPaid = 0) ne doit pas se distinguer d'un élève
/// « pareil » qu'après ouverture de sa fiche : le signal doit être visible au premier coup d'œil.
///
/// Route DÉDIÉE à la Finance plutôt qu'un champ ajouté à GetStudentsQuery (Élèves, Enseignants…) :
/// le solde de l'inscription active n'intéresse que cet écran, et l'ajouter à la liste générique
/// aurait alourdi une sous-requête pour tous ses autres usages (bulletins, effectifs...).
///
/// LECTURE ouverte à tout rôle de l'école qui tient une caisse (même garde que GetStudentBalanceQuery) —
/// composer un solde n'est pas un acte d'encaissement, seul POST /finance/payments l'est (règle #4).
/// </summary>
public record SearchStudentsForCashierQuery(string Search) : IRequest<IReadOnlyList<CashierStudentSearchResultDto>>;

public record CashierStudentSearchResultDto(
    Guid Id,
    string Matricule,
    string FullName,
    string? ClassroomName,

    /// <summary>Faux si l'élève n'a aucune inscription vivante sur l'année scolaire ACTIVE — rien à
    /// encaisser pour lui tant qu'il n'est pas (ré)inscrit ; les champs de solde valent alors 0.</summary>
    bool HasActiveEnrollment,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance);

public class SearchStudentsForCashierQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<SearchStudentsForCashierQuery, IReadOnlyList<CashierStudentSearchResultDto>>
{
    public async Task<IReadOnlyList<CashierStudentSearchResultDto>> Handle(
        SearchStudentsForCashierQuery request, CancellationToken cancellationToken)
    {
        var search = request.Search.Trim().ToLower();
        if (search.Length < 2) return [];

        var activeYearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Même remarque que GetStudentsQueryHandler : ToLower() plutôt qu'EF.Functions.ILike (Npgsql),
        // pour qu'Application reste ignorante du provider (Clean Architecture).
        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.FullName.ToLower().Contains(search) || s.Matricule.ToLower().Contains(search))
            .OrderBy(s => s.FullName)
            .Take(20)
            .Select(s => new
            {
                s.Id,
                s.Matricule,
                s.FullName,
                s.ClassroomId
            })
            .ToListAsync(cancellationToken);

        if (students.Count == 0) return [];

        var studentIds = students.Select(s => s.Id).ToList();

        // Une seule inscription VIVANTE par élève et par année (contrainte métier) : au plus une ligne
        // par StudentId ici, donc un simple dictionnaire suffit — pas besoin de désambiguïser.
        var enrollmentsById = activeYearId is { } yearId
            ? await dbContext.Enrollments.AsNoTracking()
                .Where(e => studentIds.Contains(e.StudentId) && e.SchoolYearId == yearId && e.Status != EnrollmentStatus.Cancelled)
                .Select(e => new { e.StudentId, e.TotalDue, e.AmountPaid })
                .ToDictionaryAsync(e => e.StudentId, cancellationToken)
            : [];

        var classroomIds = students.Select(s => s.ClassroomId).Distinct().ToList();
        var classroomNamesById = await dbContext.Classrooms.AsNoTracking()
            .Where(c => classroomIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return students
            .Select(s =>
            {
                var enrollment = enrollmentsById.GetValueOrDefault(s.Id);

                return new CashierStudentSearchResultDto(
                    s.Id,
                    s.Matricule,
                    s.FullName,
                    classroomNamesById.GetValueOrDefault(s.ClassroomId, "Classe supprimée"),
                    enrollment is not null,
                    enrollment?.TotalDue ?? 0m,
                    enrollment?.AmountPaid ?? 0m,
                    (enrollment?.TotalDue ?? 0m) - (enrollment?.AmountPaid ?? 0m));
            })
            .ToList();
    }
}
