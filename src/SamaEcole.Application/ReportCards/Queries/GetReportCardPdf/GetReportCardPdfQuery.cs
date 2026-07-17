using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;

/// <summary>
/// POST /report-cards/generate — ticket JGK-G03. Le bulletin PDF (A5 portrait) d'un élève pour un
/// trimestre, reproduisant docs/design-references/bulletin-reference.png (AGENTS.md règle #12).
///
/// Recalculé à la demande à partir des notes actuelles — rien n'est persisté ici (aucune entité
/// ReportCard, aucun ticket ne le demande pour cette passe) : régénérer donne toujours le bulletin à
/// jour, jamais une version figée d'un instant passé.
///
/// IAuditableRequest (JGK-H01) : « impressions » fait partie des écritures sensibles explicitement
/// listées par le journal d'audit centralisé.
/// </summary>
public record GetReportCardPdfQuery(Guid StudentId, Guid TermId) : IRequest<ReportCardPdfResult>, IAuditableRequest;

public record ReportCardPdfResult(byte[] Content, string FileName);

/// <summary>Moyenne d'un trimestre PASSÉ ou EN COURS du même exercice — null si rien n'y est encore noté.</summary>
public record ReportCardTermRecap(string TermLabel, int Order, decimal? Average);

/// <summary>
/// Toutes les données du bulletin, déjà résolues et classées — <see cref="ReportCardPdfGenerator"/> (ou
/// son équivalent Infrastructure) n'a plus qu'à mettre en page, aucun calcul ne s'y trouve.
///
/// Champs volontairement ABSENTS malgré la présence de leur case sur la référence visuelle — le
/// document IMPRIMÉ reproduit la case, restée vide, plutôt que d'inventer une donnée : T.H (aucune
/// affectation enseignant/matière n'est modélisée), Absences/Retards (aucun suivi de présence, table
/// Attendances jamais implémentée), Décision du Conseil et mentions disciplinaires — Blâme,
/// Avertissement, Tableau d'honneur, Encouragements, Félicitations — (décisions humaines du conseil de
/// classe, aucun ticket ne les capture), Classe redoublée (aucun indicateur de redoublement sur
/// Enrollment/Student), lieu de naissance (absent de Student), hiérarchie administrative Inspection
/// d'Académie/départementale (absente de School — toutes les écoles clientes n'y sont pas rattachées).
/// </summary>
public record ReportCardDto(
    string SchoolName,
    string? SchoolLogoUrl,
    string StudentFullName,
    DateOnly BirthDate,
    string ClassroomName,
    string Matricule,
    int ClassSize,
    string SchoolYearLabel,
    string TermLabel,
    int GradingScale,
    IReadOnlyList<SubjectGradeDto> Subjects,
    decimal TotalCoefficients,
    decimal TotalPoints,
    decimal GeneralAverage,
    int GeneralRank,
    IReadOnlyDictionary<Guid, int> SubjectRanks,
    string? Mention,
    IReadOnlyList<ReportCardTermRecap> TermRecaps,
    decimal? AnnualAverage,
    int? AnnualRank);

public class GetReportCardPdfQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    IReportCardPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetReportCardPdfQuery, ReportCardPdfResult>
{
    public async Task<ReportCardPdfResult> Handle(GetReportCardPdfQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : un élève ou un
        // trimestre d'une autre école y est structurellement introuvable (404, jamais un bulletin fuité).
        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.StudentId} introuvable dans votre établissement.");

        var term = await dbContext.Terms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TermId, cancellationToken)
            ?? throw new KeyNotFoundException($"Trimestre {request.TermId} introuvable dans votre établissement.");

        var classroom = await dbContext.Classrooms.AsNoTracking().FirstAsync(c => c.Id == student.ClassroomId, cancellationToken);
        var school = await dbContext.Schools.AsNoTracking().FirstAsync(s => s.Id == student.SchoolId, cancellationToken);
        var schoolYear = await dbContext.SchoolYears.AsNoTracking().FirstAsync(y => y.Id == term.SchoolYearId, cancellationToken);

        var classmateIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == student.ClassroomId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // Les trimestres de CET exercice, dans l'ordre : le récapitulatif annuel (Volume 1 §8, référence
        // visuelle) porte une ligne par trimestre + la moyenne annuelle, quel que soit leur nombre — la
        // référence en montre deux (semestres) là où notre modèle en génère trois (JGK-G01).
        var yearTerms = await dbContext.Terms.AsNoTracking()
            .Where(t => t.SchoolYearId == term.SchoolYearId)
            .OrderBy(t => t.Order)
            .ToListAsync(cancellationToken);

        // Moyennes/mention DE CE TRIMESTRE, pour l'élève ET pour chaque camarade de classe (nécessaire
        // au classement) : réutilise EXACTEMENT le calcul de JGK-G02, une seule source de vérité pour
        // « comment se calcule une moyenne ».
        var summariesThisTerm = new Dictionary<Guid, GradeSummaryDto>();
        foreach (var id in classmateIds)
        {
            summariesThisTerm[id] = await mediator.Send(new GetGradeSummaryQuery(id, term.Id), cancellationToken);
        }

        var summary = summariesThisTerm[student.Id];

        var subjectRanks = summary.Subjects.ToDictionary(
            s => s.SubjectId,
            s => RankOf(s.Average, summariesThisTerm.Values
                .Select(cs => cs.Subjects.FirstOrDefault(x => x.SubjectId == s.SubjectId)?.Average)));

        var generalRank = RankOf(summary.GeneralAverage, summariesThisTerm.Values
            .Select(cs => cs.TotalCoefficients > 0 ? (decimal?)cs.GeneralAverage : null));

        // Récapitulatif annuel : une moyenne par trimestre DÉJÀ NOTÉ de l'exercice (les trimestres à
        // venir restent null, jamais un zéro trompeur), et la moyenne annuelle qui s'ensuit.
        var annualAveragesByStudent = new Dictionary<Guid, List<decimal>>();
        var termRecaps = new List<ReportCardTermRecap>();

        foreach (var yearTerm in yearTerms)
        {
            var summariesForTerm = yearTerm.Id == term.Id
                ? summariesThisTerm
                : await LoadSummariesAsync(classmateIds, yearTerm.Id, cancellationToken);

            foreach (var (id, s) in summariesForTerm)
            {
                if (s.TotalCoefficients <= 0)
                {
                    continue;
                }

                if (!annualAveragesByStudent.TryGetValue(id, out var list))
                {
                    annualAveragesByStudent[id] = list = [];
                }

                list.Add(s.GeneralAverage);
            }

            var studentAverageThisTerm = summariesForTerm.TryGetValue(student.Id, out var studentSummary) && studentSummary.TotalCoefficients > 0
                ? studentSummary.GeneralAverage
                : (decimal?)null;

            termRecaps.Add(new ReportCardTermRecap(yearTerm.Label, yearTerm.Order, studentAverageThisTerm));
        }

        decimal? annualAverage = annualAveragesByStudent.TryGetValue(student.Id, out var mine) && mine.Count > 0
            ? mine.Average()
            : null;

        int? annualRank = annualAverage is null
            ? null
            : RankOf(annualAverage.Value, annualAveragesByStudent
                .Select(kv => kv.Value.Count > 0 ? (decimal?)kv.Value.Average() : null));

        var gradingScale = await GradingScaleGuard.ResolveScaleAsync(dbContext, cancellationToken);

        var dto = new ReportCardDto(
            school.Name,
            school.LogoUrl,
            student.FullName,
            student.BirthDate,
            classroom.Name,
            student.Matricule,
            classmateIds.Count,
            schoolYear.Label,
            term.Label,
            gradingScale,
            summary.Subjects,
            summary.TotalCoefficients,
            summary.TotalPoints,
            summary.GeneralAverage,
            generalRank,
            subjectRanks,
            summary.Mention,
            termRecaps,
            annualAverage,
            annualRank);

        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);

        var fileName = $"Bulletin-{student.Matricule}-{term.Label.Replace(' ', '-')}.pdf";
        return new ReportCardPdfResult(pdfGenerator.Generate(dto, logo), fileName);
    }

    private async Task<Dictionary<Guid, GradeSummaryDto>> LoadSummariesAsync(
        IReadOnlyList<Guid> studentIds, Guid termId, CancellationToken cancellationToken)
    {
        var summaries = new Dictionary<Guid, GradeSummaryDto>();
        foreach (var id in studentIds)
        {
            summaries[id] = await mediator.Send(new GetGradeSummaryQuery(id, termId), cancellationToken);
        }

        return summaries;
    }

    /// <summary>
    /// Classement « sportif » standard : le rang est 1 + le nombre de camarades STRICTEMENT meilleurs.
    /// Deux élèves à égalité partagent le même rang, et le rang suivant saute d'autant (1, 2, 2, 4…).
    /// Les valeurs null (rien à comparer, ce camarade n'a aucune note sur la période) sont ignorées.
    /// </summary>
    private static int RankOf(decimal mine, IEnumerable<decimal?> allAverages) =>
        allAverages.Count(v => v.HasValue && v.Value > mine) + 1;
}
