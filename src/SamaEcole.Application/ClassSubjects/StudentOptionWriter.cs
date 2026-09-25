using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects;

/// <summary>
/// Enregistre les options d'un élève pour une année (Évolution N°6) — partagé par l'inscription
/// (CreateEnrollmentCommandHandler), la fiche élève (SetStudentSubjectOptionsCommand) et l'affectation en masse
/// (AssignDefaultOptionsCommand) : une seule façon de valider un choix.
///
/// Règles : chaque option retenue doit être une matière ACTIVE d'un groupe de la classe ; une seule par groupe.
/// Un groupe laissé sans choix reçoit l'option par défaut quand <c>fillDefaults</c>, reste sans option sinon.
/// Les choix de l'année qui ne figurent plus dans le résultat sont supprimés LOGIQUEMENT (règle #6) — y compris
/// ceux d'une ancienne classe de l'élève pour la même année. N'appelle pas SaveChanges : l'appelant garde la
/// main sur sa transaction.
/// </summary>
public static class StudentOptionWriter
{
    public static async Task<IReadOnlyList<Guid>> SetAsync(
        IApplicationDbContext dbContext,
        Guid schoolId,
        string actor,
        Guid studentId,
        Guid classroomId,
        Guid schoolYearId,
        IReadOnlyCollection<Guid>? requested,
        bool fillDefaults,
        CancellationToken cancellationToken,
        string field = "SubjectOptionIds")
    {
        var groups = await ClassOptionCatalog.LoadAsync(dbContext, classroomId, schoolYearId, null, cancellationToken);
        return await SetAsync(
            dbContext, schoolId, actor, studentId, schoolYearId, groups, requested, fillDefaults, cancellationToken, field);
    }

    /// <summary>Même écriture, avec les groupes de la classe déjà chargés (affectation en masse : une lecture pour toute la classe).</summary>
    public static async Task<IReadOnlyList<Guid>> SetAsync(
        IApplicationDbContext dbContext,
        Guid schoolId,
        string actor,
        Guid studentId,
        Guid schoolYearId,
        IReadOnlyList<OptionGroupDto> groups,
        IReadOnlyCollection<Guid>? requested,
        bool fillDefaults,
        CancellationToken cancellationToken,
        string field = "SubjectOptionIds")
    {
        var final = Resolve(groups, requested ?? [], fillDefaults, field);

        var existing = await dbContext.StudentSubjectEnrollments
            .Where(e => e.StudentId == studentId && e.SchoolYearId == schoolYearId)
            .ToListAsync(cancellationToken);

        foreach (var row in existing.Where(e => !final.Contains(e.ClassSubjectId)))
        {
            row.SoftDelete(actor);
        }

        foreach (var classSubjectId in final.Where(id => existing.All(e => e.ClassSubjectId != id)))
        {
            dbContext.StudentSubjectEnrollments.Add(new StudentSubjectEnrollment
            {
                SchoolId = schoolId,
                StudentId = studentId,
                ClassSubjectId = classSubjectId,
                SchoolYearId = schoolYearId
            });
        }

        return final;
    }

    /// <summary>Choix final par groupe — pur, pour être testé seul. Lève une ValidationException (422) sur une option invalide.</summary>
    public static IReadOnlyList<Guid> Resolve(
        IReadOnlyList<OptionGroupDto> groups, IReadOnlyCollection<Guid> requested, bool fillDefaults, string field)
    {
        var groupOf = groups
            .SelectMany(g => g.Options.Select(o => (o.ClassSubjectId, Group: g.Name)))
            .ToDictionary(x => x.ClassSubjectId, x => x.Group);

        var unknown = requested.Where(id => !groupOf.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ValidationException([
                new ValidationFailure(field,
                    "Une option choisie n'appartient pas aux matières optionnelles de la classe de l'élève. "
                    + "Rechargez le formulaire : la classe ou ses options ont peut-être changé.")
            ]);
        }

        var doubled = requested.Distinct().GroupBy(id => groupOf[id]).FirstOrDefault(g => g.Count() > 1);
        if (doubled is not null)
        {
            throw new ValidationException([
                new ValidationFailure(field, $"Une seule option par groupe : choisissez une seule matière pour « {doubled.Key} ».")
            ]);
        }

        var final = new List<Guid>();
        foreach (var group in groups)
        {
            var choice = requested.FirstOrDefault(id => groupOf[id] == group.Name);
            if (choice != Guid.Empty)
            {
                final.Add(choice);
            }
            else if (fillDefaults)
            {
                final.Add(group.DefaultClassSubjectId);
            }
        }

        return final;
    }
}
