using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.OptionalSubjects;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;

namespace SamaEcole.Application.StateIntegration.Queries.GetSkillsBookletPdf;

/// <summary>
/// Construit le livret de compétences (Volume 1 §23.6).
///
/// LE LIVRET EST UNE VUE PLURI-TRIMESTRES d'un objet que le produit ne sait construire que
/// trimestre par trimestre : la grille d'évaluation APC (<see cref="EvaluationStructureBuilder"/>).
/// On l'appelle donc UNE FOIS PAR TRIMESTRE de l'année, puis on fusionne les résultats sur la
/// structure — domaines et compétences — du premier trimestre qui en renvoie une. Réutiliser ce
/// builder plutôt que réécrire une seconde lecture des matières garantit que le livret et le bulletin
/// APC décrivent EXACTEMENT la même grille : une divergence entre les deux, sur le document qui suit
/// l'élève d'école en école, serait indéfendable.
///
/// Un niveau SANS grille APC (secondaire ordinaire) fait échouer la requête avec un message clair,
/// plutôt que de produire un livret vide : ce document n'a de sens que pour l'approche par
/// compétences.
/// </summary>
public class GetSkillsBookletPdfQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ISkillsBookletPdfGenerator pdfGenerator,
    ISchoolLogoProvider imageProvider)
    : IRequestHandler<GetSkillsBookletPdfQuery, SkillsBookletPdfResult>
{
    public async Task<SkillsBookletPdfResult> Handle(
        GetSkillsBookletPdfQuery request, CancellationToken cancellationToken)
    {
        _ = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement dans le jeton d'authentification.");

        // Global Query Filter + RLS : un élève d'une autre école est structurellement introuvable ici.
        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken)
            ?? throw new KeyNotFoundException("Élève introuvable dans votre établissement.");

        var schoolYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException("Année scolaire introuvable dans votre établissement.");

        var classroom = await dbContext.Classrooms.AsNoTracking()
            .FirstAsync(c => c.Id == student.ClassroomId, cancellationToken);

        var school = await dbContext.Schools.AsNoTracking()
            .FirstAsync(s => s.Id == student.SchoolId, cancellationToken);

        var settings = await dbContext.SchoolSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == student.SchoolId, cancellationToken);

        var terms = await dbContext.Terms.AsNoTracking()
            .Where(t => t.SchoolYearId == request.SchoolYearId)
            .OrderBy(t => t.Order)
            .Select(t => new { t.Id, t.Label, t.SchoolYearId })
            .ToListAsync(cancellationToken);

        if (terms.Count == 0)
        {
            throw new BusinessRuleException(
                "Cette année scolaire ne comporte aucun trimestre : un livret de compétences n'aurait "
                + "aucune colonne de période.");
        }

        var gradingScale = await GradingScaleGuard.ResolveScaleForClassroomAsync(
            dbContext, student.ClassroomId, cancellationToken);
        var mentionScale = await MentionScale.ResolveAsync(dbContext, cancellationToken);

        // Une grille par trimestre. Index par ORDRE des trimestres : structurePerTerm[i] correspond à
        // terms[i], et vaut null pour un trimestre où le niveau ne déclare (encore) aucune hiérarchie.
        var structurePerTerm = new List<EvaluationStructureDto?>();
        foreach (var term in terms)
        {
            var summary = await mediator.Send(
                new GetGradeSummaryQuery(student.Id, term.Id), cancellationToken);

            var exemptions = await SubjectExemptions.ForStudentAsync(
                dbContext, student.Id, term.SchoolYearId, cancellationToken);

            structurePerTerm.Add(await EvaluationStructureBuilder.BuildAsync(
                dbContext, classroom.Level, gradingScale, summary.Subjects, mentionScale, exemptions, cancellationToken));
        }

        var canonical = structurePerTerm.FirstOrDefault(s => s is not null)
            ?? throw new BusinessRuleException(
                "Le niveau de cette classe n'a pas de grille de compétences configurée : le livret de "
                + "compétences ne s'applique qu'à l'approche par compétences (APC). Utilisez le bulletin "
                + "de notes.");

        var domains = BuildDomains(canonical, structurePerTerm, terms.Count);

        // Appréciation globale : l'observation du conseil du DERNIER trimestre de l'année, si elle a
        // été saisie. Null sinon — le cadre s'imprime vide, jamais rempli d'une phrase générée.
        var lastTermId = terms[^1].Id;
        var overallAppreciation = await dbContext.ReportCardRemarks.AsNoTracking()
            .Where(r => r.StudentId == student.Id && r.TermId == lastTermId)
            .Select(r => r.Observations)
            .FirstOrDefaultAsync(cancellationToken);

        var model = new SkillsBookletModel(
            SchoolName: school.Name,
            InspectionAcademie: school.InspectionAcademie,
            InspectionEducationFormation: school.InspectionEducationFormation,
            HeadingPrefix: SchoolHeading.PrefixFor(classroom.Cycle),
            HeadingName: SchoolHeading.StripCyclePrefix(school.NomLycee),
            StudentFullName: student.FullName,
            Matricule: student.Matricule,
            IenNumber: student.IenNumber,
            BirthDate: student.BirthDate,
            BirthPlace: student.BirthPlace,
            ClassroomName: classroom.Name,
            Level: classroom.Level,
            SchoolYearLabel: schoolYear.Label,
            TermLabels: terms.Select(t => t.Label).ToList(),
            Domains: domains,
            OverallAppreciation: overallAppreciation,
            DirectorSignature: await imageProvider.TryFetchAsync(settings?.DirectorSignatureUrl, cancellationToken),
            OfficialStamp: await imageProvider.TryFetchAsync(settings?.OfficialStampUrl, cancellationToken));

        var fileName = $"Livret-Competences-{student.Matricule}-{schoolYear.Label.Replace('/', '-').Replace(' ', '-')}.pdf";
        return new SkillsBookletPdfResult(pdfGenerator.Generate(model), fileName);
    }

    /// <summary>
    /// Fusionne les grilles trimestrielles sur la structure canonique. Pour chaque compétence, on
    /// relève le niveau d'acquisition atteint à chaque trimestre — <c>null</c> quand ce trimestre-là
    /// n'a pas de note pour cette ligne (case vide au livret, jamais « non acquis »).
    ///
    /// Le rapprochement se fait sur le COUPLE (nom de domaine, libellé d'activité). Une clé plus
    /// robuste — l'identifiant de matière — n'est pas disponible de façon stable ici :
    /// <see cref="EvaluationLineDto.SubjectId"/> porte l'identité du domaine parent pour toutes ses
    /// activités, pas celle de la ligne. Les libellés d'une grille APC sont figés par configuration
    /// d'école ; ce rapprochement textuel est donc sûr en pratique, et une activité renommée en cours
    /// d'année (cas théorique) apparaîtrait simplement comme deux lignes distinctes.
    /// </summary>
    private static List<SkillsBookletDomain> BuildDomains(
        EvaluationStructureDto canonical,
        IReadOnlyList<EvaluationStructureDto?> structurePerTerm,
        int termCount)
    {
        // Un index par trimestre : (domaine ┆ activité) → pourcentage de réussite converti en niveau.
        var levelsByTerm = structurePerTerm
            .Select(structure => structure is null
                ? new Dictionary<(string Domain, string Line), SkillAcquisitionLevel?>()
                : structure.Groups
                    .SelectMany(g => g.Lines.Select(l => (g.Name, Line: l.Label ?? g.Name, l.Score, l.MaxScore)))
                    .GroupBy(x => (x.Name, x.Line))
                    .ToDictionary(
                        grp => grp.Key,
                        grp => SkillAcquisition.FromScore(grp.First().Score, grp.First().MaxScore)))
            .ToList();

        return canonical.Groups.Select(group =>
        {
            var competencies = group.Lines.Select(line =>
            {
                var lineLabel = line.Label ?? group.Name;

                var levels = new List<SkillAcquisitionLevel?>(termCount);
                for (var termIndex = 0; termIndex < termCount; termIndex++)
                {
                    levels.Add(levelsByTerm[termIndex].GetValueOrDefault((group.Name, lineLabel)));
                }

                return new SkillsBookletCompetency(lineLabel, levels);
            }).ToList();

            return new SkillsBookletDomain(group.Name, competencies);
        }).ToList();
    }
}
