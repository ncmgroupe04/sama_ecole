using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ClassSubjects;

/// <param name="SubjectsCreated">Matières créées dans l'établissement parce qu'aucune ne correspondait à une ligne du modèle.</param>
/// <param name="SubjectsAdded">Matières ajoutées au programme de la classe.</param>
/// <param name="SubjectsRestored">Matières du modèle réactivées (réinitialisation).</param>
/// <param name="SubjectsDeactivated">Matières hors modèle et non ajoutées par le Directeur, désactivées (réinitialisation).</param>
/// <param name="CoefficientsSet">Coefficients de classe posés ou corrigés pour l'année active.</param>
/// <param name="CoefficientsCleared">Coefficients de classe retirés parce que la valeur héritée vaut déjà la valeur officielle.</param>
/// <param name="NoActiveYear">Vrai si aucune année n'est active : le programme est posé, les coefficients ne le sont pas.</param>
public sealed record ClassTemplateReport(
    string? Series,
    int SubjectsCreated,
    int SubjectsAdded,
    int SubjectsRestored,
    int SubjectsDeactivated,
    int CoefficientsSet,
    int CoefficientsCleared,
    bool NoActiveYear)
{
    public static ClassTemplateReport Empty(string? series) => new(series, 0, 0, 0, 0, 0, 0, false);
}

/// <summary>
/// Recopie le modèle national de la série d'une classe dans son programme (Évolution N°6, étape A) : une ligne
/// <see cref="ClassSubject"/> par matière du modèle, avec son groupe d'options, et le coefficient officiel en
/// surcharge de CLASSE pour l'année active là où la valeur héritée diffère.
///
/// Deux modes, un seul algorithme :
///   * création de la classe (<c>enforceOfficial = false</c>) — les coefficients officiels ne s'imposent que là
///     où le Directeur n'a rien réglé pour la série : son réglage de série l'emporte ;
///   * « Réinitialiser aux coefficients officiels » (<c>enforceOfficial = true</c>) — le programme et les
///     coefficients de la classe redeviennent exactement ceux du modèle ; les matières ajoutées par le Directeur
///     (<see cref="ClassSubject.IsCustom"/>) restent au programme, leur coefficient intact.
///
/// Ne modifie JAMAIS <c>Subject.Coefficient</c> ni une surcharge de série : seule la classe visée change, les
/// autres classes de la même série gardent leurs bulletins. Une matière du modèle absente de l'établissement
/// est CRÉÉE au niveau de la classe, avec le coefficient officiel. N'appelle pas SaveChanges.
/// </summary>
public class ClassSubjectTemplateInjector(
    IApplicationDbContext dbContext,
    ISeriesTemplateProvider templates,
    ICurrentUserService currentUser)
{
    public async Task<ClassTemplateReport> ApplyAsync(
        Classroom classroom, bool enforceOfficial, CancellationToken cancellationToken)
    {
        var lines = classroom.Series is { } series ? templates.For(series) : [];
        if (lines.Count == 0)
        {
            return ClassTemplateReport.Empty(classroom.Series);
        }

        var actor = currentUser.UserId?.ToString() ?? "system";

        // Matières candidates : tout sauf les domaines/activités APC et le primaire (où le coefficient est
        // neutralisé à 1). Suivies : une matière créée ici rejoint la liste pour les lignes suivantes.
        var subjects = (await dbContext.Subjects
                .Where(s => s.ParentSubjectId == null)
                .ToListAsync(cancellationToken))
            .Where(s => !CoefficientRules.IsPrimaryLevel(s.Level))
            .ToList();

        var classSubjects = await dbContext.ClassSubjects
            .Where(c => c.ClassroomId == classroom.Id)
            .ToListAsync(cancellationToken);

        var yearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var classOverrides = yearId is null
            ? []
            : await dbContext.SubjectCoefficientOverrides
                .Where(o => o.SchoolYearId == yearId && o.ClassroomId == classroom.Id)
                .ToListAsync(cancellationToken);

        var seriesOverrides = yearId is null
            ? new Dictionary<Guid, decimal>()
            : await dbContext.SubjectCoefficientOverrides.AsNoTracking()
                .Where(o => o.SchoolYearId == yearId && o.Series == classroom.Series)
                .ToDictionaryAsync(o => o.SubjectId, o => o.Coefficient, cancellationToken);

        int created = 0, added = 0, restored = 0, deactivated = 0, set = 0, cleared = 0;
        var order = 0;
        var matched = new HashSet<Guid>();

        foreach (var line in lines)
        {
            var subject = Pick(line, subjects, classroom.Level);
            if (subject is null)
            {
                subject = new Subject
                {
                    SchoolId = classroom.SchoolId,
                    Name = line.Label,
                    Level = classroom.Level,
                    Coefficient = line.Coefficient
                };
                dbContext.Subjects.Add(subject);
                subjects.Add(subject);
                created++;
            }

            // Deux lignes du modèle ne désignent jamais la même matière (alias uniques par série) ; si l'école a
            // nommé une matière de façon à couvrir deux lignes, la seconde est ignorée plutôt que dédoublée.
            if (!matched.Add(subject.Id))
            {
                continue;
            }

            var row = classSubjects.FirstOrDefault(c => c.SubjectId == subject.Id);
            if (row is null)
            {
                row = new ClassSubject
                {
                    SchoolId = classroom.SchoolId,
                    ClassroomId = classroom.Id,
                    SubjectId = subject.Id,
                    OptionGroup = line.OptionGroup,
                    IsActive = true,
                    DisplayOrder = order
                };
                dbContext.ClassSubjects.Add(row);
                classSubjects.Add(row);
                added++;
            }
            else if (enforceOfficial)
            {
                if (!row.IsActive) restored++;
                row.IsActive = true;
                row.IsCustom = false;
                row.OptionGroup = line.OptionGroup;
                row.DisplayOrder = order;
            }

            order++;

            if (yearId is null)
            {
                continue;
            }

            var classOverride = classOverrides.FirstOrDefault(o => o.SubjectId == subject.Id);
            var seriesOverride = seriesOverrides.TryGetValue(subject.Id, out var s) ? s : (decimal?)null;
            var inherited = seriesOverride ?? subject.Coefficient;

            if (enforceOfficial)
            {
                if (inherited == line.Coefficient)
                {
                    if (classOverride is not null)
                    {
                        classOverride.SoftDelete(actor);
                        cleared++;
                    }
                }
                else if (classOverride is null)
                {
                    AddClassOverride(classroom, yearId.Value, subject.Id, line.Coefficient);
                    set++;
                }
                else if (classOverride.Coefficient != line.Coefficient)
                {
                    classOverride.Coefficient = line.Coefficient;
                    set++;
                }
            }
            else if (classOverride is null && seriesOverride is null && subject.Coefficient != line.Coefficient)
            {
                AddClassOverride(classroom, yearId.Value, subject.Id, line.Coefficient);
                set++;
            }
        }

        if (enforceOfficial)
        {
            // Hors modèle : les matières ajoutées par le Directeur restent (à la suite) ; celles d'un ancien modèle
            // (la série a changé) sont désactivées — jamais supprimées, leurs notes restent en base.
            foreach (var row in classSubjects.Where(c => !matched.Contains(c.SubjectId)).OrderBy(c => c.DisplayOrder))
            {
                if (row.IsCustom)
                {
                    row.DisplayOrder = order++;
                }
                else if (row.IsActive)
                {
                    row.IsActive = false;
                    deactivated++;
                }
            }
        }

        return new ClassTemplateReport(
            classroom.Series, created, added, restored, deactivated, set, cleared, yearId is null);
    }

    private void AddClassOverride(Classroom classroom, Guid yearId, Guid subjectId, decimal coefficient)
        => dbContext.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
        {
            SchoolId = classroom.SchoolId,
            SchoolYearId = yearId,
            SubjectId = subjectId,
            ClassroomId = classroom.Id,
            Coefficient = coefficient
        });

    /// <summary>
    /// La matière de l'établissement qui porte une ligne du modèle : de préférence au niveau de la classe, puis au
    /// lycée, puis à un niveau libre non reconnu (« Terminale S2 ») ; à égalité, la première par nom. Une matière
    /// explicitement de COLLÈGE n'est jamais reprise : la lier à une classe de lycée mêlerait les deux grilles —
    /// une matière de lycée est créée à la place. Null si aucune ne convient.
    /// </summary>
    public static Subject? Pick(TemplateLine line, IEnumerable<Subject> subjects, string classroomLevel)
    {
        var level = SeriesCoefficientTemplates.NormalizeName(classroomLevel);

        return subjects
            .Where(s => line.Matches(s.Name) && !IsCollegeLevel(s.Level))
            .OrderBy(s => SeriesCoefficientTemplates.NormalizeName(s.Level) == level ? 0
                : ClassroomCycle.CycleFor(s.Level) == CycleType.Lycee ? 1
                : 2)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Niveau reconnu comme collège (mêmes synonymes que <see cref="ClassroomCycle"/>).</summary>
    private static bool IsCollegeLevel(string level)
        => SeriesCoefficientTemplates.NormalizeName(level) is "COLLEGE" or "MOYEN" or "CEM";
}
