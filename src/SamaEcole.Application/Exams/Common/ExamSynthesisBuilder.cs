using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Exams.Common;

/// <summary>
/// Moyennes ANNUELLES d'un candidat par section : la moyenne des moyennes de période où la section a des notes —
/// exactement la règle de la moyenne annuelle du bulletin (ReportCardDataService : <c>mine.Average()</c> sur les
/// périodes notées), pour que le relevé d'examen et le bulletin ne se contredisent jamais. Une période où la
/// section n'a aucune note n'entre pas dans sa moyenne (jamais un zéro).
/// </summary>
public static class ExamAnnualAverages
{
    public readonly record struct Result(decimal? French, decimal? Arabic, decimal? Combined);

    public static Result Compute(IEnumerable<SectionBreakdown> terms)
    {
        var list = terms.ToList();

        return new Result(
            MeanOrNull(list.Select(t => t.French.Average)),
            MeanOrNull(list.Select(t => t.Arabic.Average)),
            MeanOrNull(list.Select(t => t.CombinedAverage)));
    }

    private static decimal? MeanOrNull(IEnumerable<decimal?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Average();
    }
}

/// <summary>
/// Construit le <see cref="ExamSynthesisModel"/> d'une session d'examen (ou d'un seul dossier) : identité du candidat
/// en français ET en arabe, identifiant national (IEN), centre et numéro de table, moyennes annuelles par section,
/// résultat de la délibération. Source unique du PV de session et de la fiche synthétique individuelle — ils ne
/// peuvent donc pas diverger.
///
/// L'IEN est repris TEL QUEL depuis la fiche élève (attribué par l'administration ou saisi) : un élève sans IEN sort
/// sans IEN, jamais un numéro inventé. Aucun appel au SIMEN : il n'existe pas d'API publique (Volume 1 §23).
/// </summary>
public class ExamSynthesisBuilder(IApplicationDbContext dbContext, ITenantProvider tenantProvider, ISender mediator)
{
    /// <param name="onlyDossierId">Un seul dossier (fiche individuelle), quel que soit son statut ; sinon tous les
    /// dossiers COMPLET, TRANSMIS ou VALIDE de la session (un dossier incomplet n'a rien à faire sur un PV).</param>
    public async Task<ExamSynthesisModel> BuildAsync(Guid examSessionId, Guid? onlyDossierId, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante.
        var session = await dbContext.ExamSessions.AsNoTracking()
            .Where(s => s.Id == examSessionId)
            .Select(s => new { s.ExamType, s.Series, s.SchoolYearId, YearLabel = s.SchoolYear.Label })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Session d'examen {examSessionId} introuvable.");

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new { s.Name, s.InspectionAcademie, s.InspectionEducationFormation })
            .SingleAsync(cancellationToken);

        var isBilingual = await dbContext.SchoolSettings.AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => (bool?)s.IsCoranModuleEnabled)
            .FirstOrDefaultAsync(cancellationToken) ?? false;

        var dossiersQuery = dbContext.ExamDossiers.AsNoTracking().Where(d => d.ExamSessionId == examSessionId);
        dossiersQuery = onlyDossierId is { } dossierId
            ? dossiersQuery.Where(d => d.Id == dossierId)
            : dossiersQuery.Where(d => d.Status == ExamDossierStatus.Complet
                                       || d.Status == ExamDossierStatus.Transmis
                                       || d.Status == ExamDossierStatus.Valide);

        var dossiers = await dossiersQuery
            .Select(d => new
            {
                d.Id, d.StudentId, d.ClassroomId, d.CandidateNumber, d.TableNumber, d.ExamCenterName, d.ExamCenterCode,
                d.Status,
                Result = d.Result == null ? null : new { d.Result.IsAdmitted, d.Result.Mention, d.Result.AverageScore, d.Result.DeliberatedOn }
            })
            .ToListAsync(cancellationToken);

        if (dossiers.Count == 0)
        {
            throw onlyDossierId is not null
                ? new KeyNotFoundException($"Dossier d'examen {onlyDossierId} introuvable.")
                : new ValidationException([new ValidationFailure("global",
                    "Aucun dossier Complet, Transmis ou Valide dans cette session : rien à imprimer.")]);
        }

        var studentIds = dossiers.Select(d => d.StudentId).Distinct().ToList();
        var students = await dbContext.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FullName, s.FullNameAr, s.Matricule, s.IenNumber, s.BirthDate, s.BirthPlace, s.Gender })
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var classroomIds = dossiers.Select(d => d.ClassroomId).Distinct().ToList();
        var classrooms = await dbContext.Classrooms.AsNoTracking()
            .Where(c => classroomIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Cycle })
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var termIds = await dbContext.Terms.AsNoTracking()
            .Where(t => t.SchoolYearId == session.SchoolYearId)
            .OrderBy(t => t.Order)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        // Une synthèse par (élève, période) : le même GetGradeSummaryQuery que le bulletin, une seule source de
        // vérité pour « comment se calcule une moyenne ». Les sections se lisent ensuite sur ces mêmes résumés.
        var summaries = new Dictionary<Guid, List<GradeSummaryDto>>();
        foreach (var studentId in studentIds)
        {
            var perTerm = new List<GradeSummaryDto>();
            foreach (var termId in termIds)
            {
                perTerm.Add(await mediator.Send(new GetGradeSummaryQuery(studentId, termId), cancellationToken));
            }

            summaries[studentId] = perTerm;
        }

        var subjectIds = summaries.Values.SelectMany(l => l).SelectMany(s => s.Subjects).Select(s => s.SubjectId).Distinct().ToList();
        var sectionTypes = await dbContext.Subjects.AsNoTracking()
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.SectionType, cancellationToken);

        var candidates = dossiers
            .Select(d =>
            {
                var student = students.GetValueOrDefault(d.StudentId);
                var classroom = classrooms.GetValueOrDefault(d.ClassroomId);

                var annual = ExamAnnualAverages.Compute(
                    summaries[d.StudentId].Select(s => SectionAverageCalculator.Compute(s.Subjects, sectionTypes)));

                return new ExamSynthesisCandidate(
                    Reference: ExamCandidateFormModelBuilder.Reference(d.Id, d.CandidateNumber),
                    StudentFullName: student?.FullName ?? "Élève supprimé",
                    StudentFullNameAr: isBilingual ? student?.FullNameAr : null,
                    Matricule: student?.Matricule ?? "",
                    IenNumber: student?.IenNumber,
                    BirthDate: student?.BirthDate ?? default,
                    BirthPlace: student?.BirthPlace ?? "",
                    Gender: student?.Gender ?? "",
                    ClassroomName: classroom?.Name ?? "Classe supprimée",
                    CandidateNumber: d.CandidateNumber,
                    TableNumber: d.TableNumber,
                    ExamCenterName: d.ExamCenterName,
                    ExamCenterCode: d.ExamCenterCode,
                    DossierStatus: d.Status,
                    GradingScale: GradingScaleGuard.ScaleForCycle(classroom?.Cycle),
                    FrenchAverage: isBilingual ? annual.French : null,
                    ArabicAverage: isBilingual ? annual.Arabic : null,
                    CombinedAverage: annual.Combined,
                    IsAdmitted: d.Result?.IsAdmitted,
                    Mention: d.Result?.Mention,
                    ExamAverage: d.Result?.AverageScore,
                    DeliberatedOn: d.Result?.DeliberatedOn);
            })
            .OrderBy(c => c.CandidateNumber is null)
            .ThenBy(c => c.CandidateNumber, StringComparer.Ordinal)
            .ThenBy(c => c.StudentFullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new ExamSynthesisModel(
            Reference: $"{session.ExamType}-{session.YearLabel}".Replace(' ', '-'),
            SchoolName: school.Name,
            InspectionAcademie: school.InspectionAcademie,
            InspectionEducationFormation: school.InspectionEducationFormation,
            ExamType: session.ExamType,
            Series: session.Series,
            SchoolYearLabel: session.YearLabel,
            IsBilingualArabic: isBilingual,
            Candidates: candidates);
    }
}
