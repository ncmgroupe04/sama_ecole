using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
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
/// document IMPRIMÉ reproduit la case, restée vide, plutôt que d'inventer une donnée : T.H (la
/// signification de cette colonne sur la référence n'est pas établie), Décision du Conseil (décision
/// humaine du conseil de classe, aucun ticket ne la capture), et Classe redoublée (aucun indicateur de
/// redoublement sur Enrollment/Student).
/// </summary>
public record ReportCardDto(
    string SchoolName,
    string? SchoolLogoUrl,

    // En-tête administratif (lignes « IA : … », « IEF : … », « LYCEE DE : … » de la référence).
    // Renseigné dans Paramètres → Établissement ; null s'imprime en ligne vide, jamais inventé.
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? NomLycee,

    string StudentFullName,
    DateOnly BirthDate,
    string? BirthPlace,
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

    // Appréciation par matière : la mention du barème de l'école atteinte par la moyenne de la matière
    // (même échelle que la mention générale — aucun vocabulaire parallèle inventé). Null si la moyenne
    // n'atteint aucun seuil : la case s'imprime vide.
    IReadOnlyDictionary<Guid, string?> SubjectAppreciations,

    string? Mention,

    // Assiduité du trimestre (JGK-D06). Null si AUCUN appel n'a été fait pour la classe sur la période :
    // les cases s'impriment avec « - » — un zéro affirmerait à tort une assiduité parfaite.
    // Absences = injustifiées seules ; TotalAbsences = justifiées + injustifiées ; Retards = pointages Late.
    int? Absences,
    int? Retards,
    int? TotalAbsences,

    IReadOnlyList<ReportCardTermRecap> TermRecaps,
    decimal? AnnualAverage,
    int? AnnualRank,

    // Distinction cochée par le conseil (Blâme… Félicitations) et son observation — saisies via
    // UpsertReportCardRemarkCommand. Null tant que rien n'a été saisi pour ce trimestre : la ligne/le
    // cadre s'imprime vide, jamais une valeur inventée.
    DisciplinaryMention? DisciplinaryMention,
    string? CouncilObservations);

public class GetReportCardPdfQueryHandler(
    ReportCardDataService dataService,
    IReportCardPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetReportCardPdfQuery, ReportCardPdfResult>
{
    public async Task<ReportCardPdfResult> Handle(GetReportCardPdfQuery request, CancellationToken cancellationToken)
    {
        var dto = await dataService.BuildAsync(request.StudentId, request.TermId, cancellationToken);
        var logo = await logoProvider.TryFetchAsync(dto.SchoolLogoUrl, cancellationToken);

        var fileName = $"Bulletin-{dto.Matricule}-{dto.TermLabel.Replace(' ', '-')}.pdf";
        return new ReportCardPdfResult(pdfGenerator.Generate(dto, logo), fileName);
    }
}

/// <summary>
/// Construit le <see cref="ReportCardDto"/> d'UN élève — tous les calculs (moyennes, rangs, récapitulatif
/// annuel, assiduité) de <see cref="GetReportCardPdfQueryHandler"/> avant sa passe finale (logo + rendu
/// PDF). Extrait pour être réutilisé TEL QUEL par les bulletins de classe (ZIP, PDF fusionné) — une
/// seule source de vérité pour « comment se calcule un bulletin », jamais une seconde copie du calcul.
/// </summary>
public class ReportCardDataService(ISender mediator, IApplicationDbContext dbContext)
{
    public async Task<ReportCardDto> BuildAsync(Guid studentId, Guid termId, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : un élève ou un
        // trimestre d'une autre école y est structurellement introuvable (404, jamais un bulletin fuité).
        var student = await dbContext.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == studentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {studentId} introuvable dans votre établissement.");

        var term = await dbContext.Terms.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == termId, cancellationToken)
            ?? throw new KeyNotFoundException($"Trimestre {termId} introuvable dans votre établissement.");

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

        // Appréciation par matière : la même échelle de mentions que la moyenne générale, appliquée à
        // la moyenne de CHAQUE matière — une seule source de vérité pour « comment se qualifie une
        // moyenne », aucun vocabulaire parallèle.
        var mentionScale = await MentionScale.ResolveAsync(dbContext, cancellationToken);
        var subjectAppreciations = summary.Subjects.ToDictionary(
            s => s.SubjectId,
            s => GradeCalculator.MentionFor(s.Average, mentionScale));

        var (absences, retards, totalAbsences) = await CountAttendanceAsync(
            student.Id, student.ClassroomId, term, cancellationToken);

        var remark = await dbContext.ReportCardRemarks.AsNoTracking()
            .FirstOrDefaultAsync(r => r.StudentId == student.Id && r.TermId == term.Id, cancellationToken);

        var dto = new ReportCardDto(
            school.Name,
            school.LogoUrl,
            school.InspectionAcademie,
            school.InspectionEducationFormation,
            school.NomLycee,
            student.FullName,
            student.BirthDate,
            student.BirthPlace,
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
            subjectAppreciations,
            summary.Mention,
            absences,
            retards,
            totalAbsences,
            termRecaps,
            annualAverage,
            annualRank,
            remark?.DisciplinaryMention,
            remark?.Observations);

        return dto;
    }

    /// <summary>
    /// Assiduité du trimestre (JGK-D06) : compte les statuts de l'élève sur les fiches d'appel de SA
    /// classe datées dans la période du trimestre. Si aucune fiche n'existe sur cette période, tout
    /// revient null — le bulletin imprime alors « - », jamais un zéro qui affirmerait à tort une
    /// assiduité parfaite alors que l'appel n'a simplement jamais été fait.
    /// </summary>
    private async Task<(int? Absences, int? Retards, int? TotalAbsences)> CountAttendanceAsync(
        Guid studentId, Guid classroomId, Term term, CancellationToken cancellationToken)
    {
        var sheetsExist = await dbContext.AttendanceSheets.AsNoTracking()
            .AnyAsync(s => s.ClassroomId == classroomId
                           && s.Date >= term.StartDate && s.Date <= term.EndDate, cancellationToken);

        if (!sheetsExist)
        {
            return (null, null, null);
        }

        var statuses = await (
            from a in dbContext.StudentAttendances.AsNoTracking()
            join sheet in dbContext.AttendanceSheets.AsNoTracking() on a.AttendanceSheetId equals sheet.Id
            where a.StudentId == studentId
                  && sheet.Date >= term.StartDate && sheet.Date <= term.EndDate
            select a.Status)
            .ToListAsync(cancellationToken);

        var unjustified = statuses.Count(s => s == AttendanceStatus.UnjustifiedAbsence);
        var justified = statuses.Count(s => s == AttendanceStatus.JustifiedAbsence);
        var late = statuses.Count(s => s == AttendanceStatus.Late);

        return (unjustified, late, unjustified + justified);
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
