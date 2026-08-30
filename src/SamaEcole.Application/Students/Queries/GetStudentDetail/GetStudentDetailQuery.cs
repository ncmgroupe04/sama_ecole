using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Queries.GetStudentDetail;

/// <summary>
/// GET /api/v1/students/{id} — ticket JGK-D02 (fiche élève complète). Agrège, pour un élève,
/// ses données d'identité, son historique scolaire (une entrée par inscription), le détail de ses
/// notes par trimestre, et l'historique de ses paiements.
///
/// LECTURE seule et bornée au tenant : le Global Query Filter EF Core + la RLS PostgreSQL (AGENTS.md
/// règle #2) rendent un élève d'une autre école structurellement invisible ici — il ressort en 404,
/// jamais servi. Aucun SchoolId n'est accepté du client (règle #10).
///
/// GRACIEUX PAR CONSTRUCTION : un élève sans inscription, sans note ou sans paiement n'est pas une
/// erreur — les sections concernées reviennent en listes vides (et le récapitulatif financier à zéro),
/// pour laisser l'UI afficher un empty-state plutôt qu'un écran cassé. Seul l'élève INTROUVABLE est un 404.
///
/// FRONTIÈRE AVEC JGK-G02 : cette requête est une LECTURE de présentation, jamais persistée — elle
/// dérive une moyenne d'affichage à partir des notes déjà stockées. La FORMULE elle-même (moyenne
/// pondérée par coefficient, attribution de la mention) est PARTAGÉE avec le recalcul officiel
/// JGK-G02 via <see cref="GradeCalculator"/> — une seule source de vérité pour le calcul, mais deux
/// lectures distinctes : celle-ci ne fige rien et ne remplace pas le bulletin officiel (JGK-G03).
/// </summary>
public record GetStudentDetailQuery(Guid StudentId) : IRequest<StudentDetailDto>;

/// <summary>Fiche complète assemblée pour l'écran de détail (les 4 sections de l'onglet fiche élève).</summary>
public record StudentDetailDto(
    StudentIdentityDto Identity,
    IReadOnlyList<AcademicHistoryEntryDto> AcademicHistory,
    IReadOnlyList<TermReportDto> Grades,

    // Null pour l'Enseignant (docs/Volume_7_Security.md, Finance) : masquer l'onglet côté UI n'est
    // qu'une commodité d'ergonomie, jamais une mesure de sécurité (Volume 5 §UX) — c'est CE champ,
    // absent de la réponse, qui empêche réellement la fuite, pas le seul masquage du bouton.
    PaymentHistoryDto? Payments,

    // Barème de l'école (10 ou 20) : les moyennes ci-dessus sont exprimées SUR CE barème (JGK-B02).
    // L'UI en a besoin pour afficher « 14,5/20 » ou « 7,2/10 » sans supposer /20 — une école en /10
    // verrait sinon des moyennes fausses.
    int GradingScale);

/// <summary>
/// Données personnelles + classe courante. Toujours renseignées (sinon 404 en amont).
/// <see cref="RowVersion"/> est le jeton xmin nécessaire à UpdateStudentCommand et
/// DeleteStudentCommand (AGENTS.md règle #5).
/// </summary>
public record StudentIdentityDto(
    Guid Id,
    string Matricule,

    // Identifiant National de l'Élève (module Intégration étatique, JGK-M01). Null tant que l'école
    // ne l'a pas saisi ou fait générer. <see cref="IsIenProvisional"/> distingue un numéro officiel
    // d'un numéro de secours fabriqué localement — l'UI l'affiche différemment, jamais comme un
    // identifiant national valide.
    string? IenNumber,
    bool IsIenProvisional,

    string FullName,
    DateOnly BirthDate,
    string? BirthPlace,
    string Gender,
    Guid ClassroomId,
    string ClassroomName,

    /// <summary>URL externe BRUTE (voir StudentListItem.PhotoUrl) — round-trip fidèle pour l'édition.</summary>
    string? PhotoUrl,

    /// <summary>Feature B — valeur À AFFICHER (voir StudentListItem.PhotoDisplayUrl).</summary>
    string? PhotoDisplayUrl,

    string? GuardianName,
    string? GuardianPhone,
    string? GuardianEmail,
    string? Address,
    uint RowVersion);

/// <summary>
/// Une ligne d'historique par inscription (année + classe + type + statut). Les inscriptions annulées
/// figurent dans l'historique (avec leur statut) mais ne comptent pas dans le récapitulatif financier.
/// <see cref="RowVersion"/> est le jeton xmin nécessaire à CancelEnrollmentCommand et
/// ChangeEnrollmentStatusCommand (AGENTS.md règle #5) — c'est depuis cette ligne d'historique que
/// l'UI propose « Annuler l'inscription » / « Déclarer un abandon ou un transfert ».
/// </summary>
public record AcademicHistoryEntryDto(
    Guid EnrollmentId,
    Guid SchoolYearId,
    string SchoolYearLabel,
    string ClassroomName,
    string EnrollmentType,
    string Status,
    bool IsActiveYear,
    decimal? GeneralAverage,
    DateTimeOffset EnrolledAt,
    uint RowVersion);

/// <summary>Bloc de notes d'un trimestre : le bulletin lisible matière par matière, plus la synthèse.</summary>
public record TermReportDto(
    Guid TermId,
    string TermLabel,
    int TermOrder,
    Guid SchoolYearId,
    string SchoolYearLabel,
    IReadOnlyList<SubjectGradeDto> Subjects,
    decimal? GeneralAverage,
    string? Mention);

/// <summary>
/// Une matière du bulletin : Devoir1, Devoir2, Composition, moyenne des devoirs, moyenne de matière et
/// moyenne pondérée par le coefficient.
/// </summary>
public record SubjectGradeDto(
    Guid SubjectId,
    string SubjectName,
    decimal Coefficient,
    decimal? Devoir1,
    decimal? Devoir2,
    decimal? Composition,
    decimal? DevoirAverage,
    decimal? Average,
    decimal? WeightedAverage,

    // Barème de la matière — la colonne « Sur » des grilles par compétences. Toujours résolu (jamais
    // null) : la valeur fixée sur la matière, ou à défaut celle du cycle de la classe.
    // <see cref="Average"/> est exprimée SUR CE BARÈME ; <see cref="WeightedAverage"/>, lui, est déjà
    // ramené à celui de la fiche.
    decimal MaxScore = 20m);

/// <summary>Récapitulatif financier de l'élève + la liste de ses versements (le solde vit sur l'inscription).</summary>
public record PaymentHistoryDto(
    decimal TotalDue,
    decimal TotalPaid,
    decimal RemainingBalance,
    IReadOnlyList<PaymentEntryDto> Entries);

/// <summary>Un versement encaissé. <c>BalanceAfter</c> est le solde figé au jour de l'encaissement (JGK-F02).</summary>
public record PaymentEntryDto(
    Guid PaymentId,
    string ReceiptNumber,
    string SchoolYearLabel,
    decimal Amount,
    decimal BalanceAfter,
    string Method,
    string Status,
    DateTimeOffset PaidAt);

public class GetStudentDetailQueryHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<GetStudentDetailQuery, StudentDetailDto>
{
    public async Task<StudentDetailDto> Handle(GetStudentDetailQuery request, CancellationToken cancellationToken)
    {
        // 1) Identité — et point de contrôle d'existence. Un élève d'une autre école est déjà filtré par
        //    le Global Query Filter + la RLS : introuvable ici veut dire 404, jamais fuite inter-tenant.
        var student = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new
            {
                s.Id,
                s.Matricule,
                s.IenNumber,
                s.IsIenProvisional,
                s.FullName,
                s.BirthDate,
                s.BirthPlace,
                s.Gender,
                s.ClassroomId,
                s.PhotoUrl,
                s.PhotoData,
                s.GuardianName,
                s.GuardianPhone,
                s.GuardianEmail,
                s.Address,
                RowVersion = EF.Property<uint>(s, "xmin"),

                // Sous-requête pour le nom de classe : une classe supprimée (soft delete) sort du Global
                // Query Filter — on l'affiche alors explicitement plutôt que de perdre la ligne (même
                // parti pris que GetStudentsQueryHandler).
                ClassroomName = dbContext.Classrooms.AsNoTracking()
                    .Where(c => c.Id == s.ClassroomId)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? "Classe supprimée"
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.StudentId} introuvable.");

        var identity = new StudentIdentityDto(
            student.Id,
            student.Matricule,
            student.IenNumber,
            student.IsIenProvisional,
            student.FullName,
            student.BirthDate,
            student.BirthPlace,
            student.Gender,
            student.ClassroomId,
            student.ClassroomName,
            student.PhotoUrl,
            PhotoDisplay.ToDisplayUrl(student.PhotoData, student.PhotoUrl),
            student.GuardianName,
            student.GuardianPhone,
            student.GuardianEmail,
            student.Address,
            student.RowVersion);

        // Barème du CYCLE de la classe de l'élève (Primaire /10, Collège & Lycée /20), et NON un réglage
        // global d'école : il pilote l'affichage des moyennes sur la fiche (« /10 » ou « /20 ») et, pour
        // le secondaire, l'échelle des mentions par défaut. Un CM2 affiche /10, une 3e /20, même école.
        // Cohérent avec le bulletin (ReportCardDataService) et le recalcul JGK-G02 : une seule vérité.
        var gradingScale = await GradingScaleGuard.ResolveScaleForClassroomAsync(
            dbContext, student.ClassroomId, cancellationToken);

        // 2) Notes — d'abord, car l'historique scolaire réutilise la moyenne annuelle qui s'en déduit.
        var grades = await BuildTermReportsAsync(request.StudentId, gradingScale, cancellationToken);

        // 3) Inscriptions → historique scolaire + assiette du récapitulatif financier.
        var enrollments = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.StudentId == request.StudentId
            orderby y.StartDate descending
            select new
            {
                e.Id,
                SchoolYearId = y.Id,
                SchoolYearLabel = y.Label,
                y.IsActive,
                e.Type,
                e.Status,
                e.TotalDue,
                e.AmountPaid,
                e.EnrolledAt,
                RowVersion = EF.Property<uint>(e, "xmin"),
                ClassroomName = dbContext.Classrooms.AsNoTracking()
                    .Where(c => c.Id == e.ClassroomId)
                    .Select(c => c.Name)
                    .FirstOrDefault() ?? "Classe supprimée"
            })
            .ToListAsync(cancellationToken);

        // Moyenne annuelle d'affichage = moyenne des moyennes trimestrielles disponibles de l'année.
        // Dérivée pour la seule présentation (frontière JGK-G02) — null tant qu'aucun trimestre n'a de note.
        var averageByYear = grades
            .Where(t => t.GeneralAverage is not null)
            .GroupBy(t => t.SchoolYearId)
            .ToDictionary(g => g.Key, g => (decimal?)g.Average(t => t.GeneralAverage!.Value));

        var academicHistory = enrollments
            .Select(e => new AcademicHistoryEntryDto(
                e.Id,
                e.SchoolYearId,
                e.SchoolYearLabel,
                e.ClassroomName,
                e.Type.ToString(),
                e.Status.ToString(),
                e.IsActive,
                averageByYear.GetValueOrDefault(e.SchoolYearId),
                e.EnrolledAt,
                e.RowVersion))
            .ToList();

        // 4) Paiements — jamais pour l'Enseignant (Volume 7 « Finance », Volume 1 §« Sans accès »).
        // Absent de la requête plutôt que masqué côté UI : un accès direct à l'URL ne doit rien exposer.
        PaymentHistoryDto? payments = null;
        if (currentUser.Role != Role.Enseignant)
        {
            // Récapitulatif financier : le dû et le versé s'entendent hors inscriptions annulées (même
            // périmètre que le tableau de bord Finance, JGK-F04). Le solde reste TotalDue − AmountPaid.
            var financial = enrollments.Where(e => e.Status != EnrollmentStatus.Cancelled).ToList();
            var totalDue = financial.Sum(e => e.TotalDue);
            var totalPaid = financial.Sum(e => e.AmountPaid);

            var paymentEntries = await BuildPaymentEntriesAsync(request.StudentId, cancellationToken);
            payments = new PaymentHistoryDto(totalDue, totalPaid, totalDue - totalPaid, paymentEntries);
        }

        return new StudentDetailDto(identity, academicHistory, grades, payments, gradingScale);
    }

    /// <summary>Versements encaissés, reliés à l'élève PAR l'inscription (le Payment ne porte pas de StudentId).</summary>
    private async Task<IReadOnlyList<PaymentEntryDto>> BuildPaymentEntriesAsync(
        Guid studentId, CancellationToken cancellationToken) =>
        await (
            from p in dbContext.Payments.AsNoTracking()
            join e in dbContext.Enrollments.AsNoTracking() on p.EnrollmentId equals e.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.StudentId == studentId
            orderby p.PaidAt descending
            select new PaymentEntryDto(
                p.Id,
                p.ReceiptNumber,
                y.Label,
                p.Amount,
                p.BalanceAfter,
                p.Method.ToString(),
                p.Status.ToString(),
                p.PaidAt))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Assemble le bulletin lisible : une entrée par trimestre ayant au moins une note, chaque matière
    /// portant son Devoir et sa Composition. La moyenne par matière, la moyenne générale pondérée et la
    /// mention utilisent <see cref="GradeCalculator"/> — la même formule que le recalcul officiel JGK-G02.
    /// </summary>
    private async Task<IReadOnlyList<TermReportDto>> BuildTermReportsAsync(
        Guid studentId, int gradingScale, CancellationToken cancellationToken)
    {
        var gradeRows = await (
            from g in dbContext.Grades.AsNoTracking()
            join subj in dbContext.Subjects.AsNoTracking() on g.SubjectId equals subj.Id
            join t in dbContext.Terms.AsNoTracking() on g.TermId equals t.Id
            join y in dbContext.SchoolYears.AsNoTracking() on t.SchoolYearId equals y.Id
            where g.StudentId == studentId
            select new
            {
                TermId = t.Id,
                TermLabel = t.Label,
                TermOrder = t.Order,
                SchoolYearId = y.Id,
                SchoolYearLabel = y.Label,
                SubjectId = subj.Id,
                SubjectName = subj.Name,
                subj.Coefficient,
                subj.MaxScore,
                g.EvaluationType,
                g.Value
            })
            .ToListAsync(cancellationToken);

        // Aucune note : liste vide, l'onglet Notes affichera son empty-state. Pas d'objet fantôme.
        if (gradeRows.Count == 0)
        {
            return [];
        }

        // Le Primaire (barème /10) calcule une moyenne SIMPLE (coefficients neutralisés à 1) et n'attribue
        // PAS de mention ; le secondaire garde la pondération /20 et ses mentions — même règle que
        // GetGradeSummaryQueryHandler (JGK-G02), pour que la fiche ne contredise jamais le bulletin.
        // gradingScale est déjà résolu par cycle en amont : 10 ⟺ Primaire, un seul signal pour la fiche.
        var isPrimaire = gradingScale == 10;

        // Mentions personnalisées de l'école (JGK-G02), ou barème par défaut tant que rien n'est stocké —
        // même principe de « valeurs par défaut si rien en base » que GetSchoolSettingsQueryHandler.
        var mentions = await ResolveMentionsAsync(gradingScale, cancellationToken);

        var termReports = gradeRows
            .GroupBy(r => new { r.TermId, r.TermLabel, r.TermOrder, r.SchoolYearId, r.SchoolYearLabel })
            .Select(term =>
            {
                var subjects = term
                    .GroupBy(r => new { r.SubjectId, r.SubjectName, r.Coefficient, r.MaxScore })
                    .Select(subject =>
                    {
                        // Une note au plus par (matière, type d'évaluation) — la contrainte d'unicité de
                        // JGK-G01 le garantit ; FirstOrDefault reste tolérant côté lecture.
                        var devoir1 = subject
                            .Where(r => r.EvaluationType == EvaluationType.Devoir1)
                            .Select(r => (decimal?)r.Value)
                            .FirstOrDefault();
                        var devoir2 = subject
                            .Where(r => r.EvaluationType == EvaluationType.Devoir2)
                            .Select(r => (decimal?)r.Value)
                            .FirstOrDefault();
                        var composition = subject
                            .Where(r => r.EvaluationType == EvaluationType.Composition)
                            .Select(r => (decimal?)r.Value)
                            .FirstOrDefault();
                        var devoirAverage = GradeCalculator.DevoirAverage(devoir1, devoir2);
                        var average = GradeCalculator.SubjectAverage(devoir1, devoir2, composition);

                        // Primaire : coefficient neutralisé à 1 → la moyenne générale (ligne ci-dessous,
                        // via WeightedGeneralAverage) devient une moyenne simple. Le coefficient réel de
                        // la matière est ignoré (le primaire n'a pas de système de coefficients).
                        var coefficient = isPrimaire ? 1m : subject.Key.Coefficient;

                        // Barème PROPRE à la matière (grilles par compétences : /40, /60, /24…), à défaut
                        // celui du cycle. La moyenne affichée reste BRUTE, sur ce barème — c'est la note
                        // que l'école a saisie ; seuls les points pondérés sont ramenés au barème de la
                        // fiche, faute de quoi une ligne /60 y pèserait trois fois une ligne /20 et la
                        // fiche contredirait le bulletin (GetGradeSummaryQueryHandler applique la même
                        // transposition). Sans barème propre — toutes les données existantes — la
                        // transposition est l'identité et le calcul est celui d'avant.
                        var maxScore = GradeCalculator.EffectiveMaxScore(subject.Key.MaxScore, gradingScale);
                        var rebased = average is { } raw
                            ? GradeCalculator.Rebase(raw, maxScore, gradingScale)
                            : (decimal?)null;

                        return new SubjectGradeDto(
                            subject.Key.SubjectId,
                            subject.Key.SubjectName,
                            coefficient,
                            devoir1,
                            devoir2,
                            composition,
                            devoirAverage,
                            average,
                            rebased is { } r ? r * coefficient : null,
                            maxScore);
                    })
                    .OrderBy(s => s.SubjectName)
                    .ToList();

                var generalAverage = GradeCalculator
                    .WeightedGeneralAverage(subjects.Select(s =>
                        (s.Average is { } a ? GradeCalculator.Rebase(a, s.MaxScore, gradingScale) : (decimal?)null,
                         s.Coefficient)))
                    .GeneralAverage;

                return new TermReportDto(
                    term.Key.TermId,
                    term.Key.TermLabel,
                    term.Key.TermOrder,
                    term.Key.SchoolYearId,
                    term.Key.SchoolYearLabel,
                    subjects,
                    generalAverage,
                    // Primaire : aucune mention (réservée au secondaire), comme sur le bulletin.
                    !isPrimaire && generalAverage is { } avg ? GradeCalculator.MentionFor(avg, mentions) : null);
            })
            // Année la plus récente d'abord, puis ordre du trimestre (1, 2, 3) — cohérent avec Term.Order.
            .OrderByDescending(t => t.SchoolYearLabel)
            .ThenBy(t => t.TermOrder)
            .ToList();

        return termReports;
    }

    /// <summary>Mentions triées de la plus forte à la plus faible : la première atteinte est la bonne.</summary>
    private async Task<IReadOnlyList<(string Label, decimal MinAverage)>> ResolveMentionsAsync(
        int gradingScale, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Mentions.AsNoTracking()
            .OrderByDescending(m => m.MinAverage)
            .Select(m => new { m.Label, m.MinAverage })
            .ToListAsync(cancellationToken);

        if (stored.Count > 0)
        {
            return stored.Select(m => (m.Label, m.MinAverage)).ToList();
        }

        return MentionDefaults.ForScale(gradingScale)
            .OrderByDescending(m => m.MinAverage)
            .ToList();
    }
}
