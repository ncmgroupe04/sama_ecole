using SamaEcole.Application.Classrooms;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exemptions;
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
/// UNE ligne imprimée de la grille d'évaluation : « P. Alphabétique | 10 | 10 | Excellent ».
///
/// <see cref="Label"/> est null quand la matière n'a pas d'activités (une matière simple dans une grille
/// qui en compte par ailleurs) : son nom occupe alors les DEUX premières colonnes, sans regroupement.
/// <see cref="Score"/> est null tant que rien n'est noté — la case s'imprime vide, comme sur les grilles
/// vierges distribuées aux enseignants, jamais un zéro qui vaudrait échec.
/// <see cref="IsExempt"/> marque la matière dont l'élève est dispensé : la ligne reste dans la grille, le document
/// y imprime « Dispensé(e) » à la place de la note.
/// </summary>
public record EvaluationLineDto(
    Guid SubjectId, string? Label, decimal? Score, decimal MaxScore, string? Appreciation, bool IsExempt = false);

/// <summary>
/// Un DOMAINE et ses lignes : la première colonne du tableau porte <see cref="Name"/> une seule fois,
/// fusionnée sur toute la hauteur du groupe (RowSpan = <c>Lines.Count</c>).
/// </summary>
public record EvaluationGroupDto(Guid SubjectId, string Name, IReadOnlyList<EvaluationLineDto> Lines);

/// <summary>
/// La grille d'évaluation COMPLÈTE d'un bulletin — configurée par l'école (Subject.ParentSubjectId,
/// MaxScore, DisplayOrder, Column1Header/Column2Header), et non déduite des notes saisies : le tableau
/// imprime TOUTES les lignes de la grille du niveau, notées ou non, exactement comme les modèles
/// officiels du primaire.
///
/// Null sur <see cref="ReportCardDto.EvaluationStructure"/> quand le niveau n'a aucune hiérarchie —
/// le bulletin retombe alors sur ses tableaux d'origine (secondaire, primaire simple), inchangés.
/// </summary>
public record EvaluationStructureDto(
    string Column1Header,
    string Column2Header,
    IReadOnlyList<EvaluationGroupDto> Groups)
{
    /// <summary>Nombre de lignes imprimées — ce qui dimensionne l'interligne du tableau sur la page A5.</summary>
    public int LineCount => Groups.Sum(g => g.Lines.Count);
}

/// <summary>
/// Toutes les données du bulletin, déjà résolues et classées — <see cref="ReportCardPdfGenerator"/> (ou
/// son équivalent Infrastructure) n'a plus qu'à mettre en page, aucun calcul ne s'y trouve.
///
/// Plus aucune case du gabarit n'est laissée vide faute de donnée : la dernière — T.H, dont la
/// signification n'avait jamais été établie — est désormais alimentée par <see cref="SubjectHonors"/>
/// (Tableau d'Honneur par matière). La case « Classe redoublée » l'est depuis <see cref="IsRepeating"/>
/// (feature F, Enrollment.IsRepeating).
/// </summary>
public record ReportCardDto(
    string SchoolName,
    string? SchoolLogoUrl,

    // En-tête administratif (lignes « IA : … », « IEF : … », « <cycle> DE : … » de la référence).
    // Renseigné dans Paramètres → Établissement ; null s'imprime en ligne vide, jamais inventé.
    string? InspectionAcademie,
    string? InspectionEducationFormation,

    // Troisième ligne, résolue par SchoolHeading : le préfixe suit le CYCLE de la classe de l'élève
    // (« ÉCOLE ÉLÉMENTAIRE DE » / « COLLÈGE DE » / « LYCÉE DE »), et non un réglage global — un même
    // établissement édite des bulletins de CM2 et de Terminale. HeadingName est le nom saisi par
    // l'école, débarrassé du préfixe de cycle qu'elle avait pu y écrire elle-même.
    string HeadingPrefix,
    string? HeadingName,

    string StudentFullName,
    DateOnly BirthDate,
    string? BirthPlace,
    string ClassroomName,

    // Cycle de la classe. Gouverne le libellé de l'en-tête (via HeadingPrefix) ET la variante de
    // tableau retenue par ReportCardDocument — qui déduisait jusqu'ici le primaire de GradingScale == 10,
    // un proxy incapable de distinguer Collège de Lycée (tous deux /20).
    CycleType Cycle,

    string Matricule,
    int ClassSize,

    // Classe redoublée (feature F) : coché [X] sur le bulletin si l'inscription de l'élève pour cet
    // exercice porte Enrollment.IsRepeating. False (case vide) si l'élève ne redouble pas, ou s'il n'a
    // aucune inscription active sur l'année du trimestre.
    bool IsRepeating,
    string SchoolYearLabel,
    string TermLabel,
    int GradingScale,
    IReadOnlyList<SubjectGradeDto> Subjects,
    decimal TotalCoefficients,
    decimal TotalPoints,
    decimal GeneralAverage,
    int GeneralRank,
    IReadOnlyDictionary<Guid, int> SubjectRanks,

    // Appréciation par matière : le vocabulaire fixe des bulletins sénégalais (Faible, Insuffisant,
    // Moyen, Assez Bien, Bon Travail, Très Bien), et NON l'échelle de mentions configurable de l'école,
    // qui qualifie la seule moyenne générale — voir SubjectAppreciationScale pour ce partage. Toute
    // matière notée en reçoit une : le barème a un plancher, contrairement aux mentions.
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

    // Distinction cochée par le conseil (Blâme… Félicitations), sa décision (Admis/Redouble/Exclusion)
    // et son observation — saisies via UpsertReportCardRemarkCommand. Null tant que rien n'a été saisi
    // pour ce trimestre : la ligne/case/cadre s'imprime vide, jamais une valeur inventée.
    DisciplinaryMention? DisciplinaryMention,
    CouncilDecision? CouncilDecision,
    string? CouncilObservations,

    // Cachet et signature du Chef d'Établissement (Paramètres → Établissement, SchoolSettings), déjà
    // saisis par le Directeur mais jusqu'ici jamais imprimés sur aucun document : le bulletin dessinait
    // un simple cercle en pointillés à la place. Null s'imprime comme avant (cercle/rien), jamais une
    // image inventée.
    string? DirectorSignatureUrl = null,
    string? OfficialStampUrl = null,

    // Classe PASSERELLE / ACCÉLÉRÉE (option) : mention imprimée en en-tête du bulletin et du PV de
    // délibération — « Cursus Accéléré Passerelle — CI → CP » (ClassroomPromotion.AcceleratedPathLabel).
    // Null pour une classe ordinaire : rien ne s'imprime, le gabarit de la référence visuelle est
    // strictement inchangé (AGENTS.md règle #12).
    string? AcceleratedPathLabel = null,

    // Niveaux effectivement validés par cet élève au titre de l'année, une fois la décision du conseil
    // prononcée (ClassroomPromotion.ValidatedLevels) : DEUX pour un élève admis en classe passerelle, un
    // seul en classe ordinaire, aucun tant que le conseil n'a pas statué ou s'il ne l'a pas admis.
    IReadOnlyList<string>? ValidatedLevels = null,

    // Grille d'évaluation par compétences (APC) configurée par l'école pour le NIVEAU de la classe.
    // Null — le cas de tout niveau dont les matières sont restées plates, c'est-à-dire de toutes les
    // données antérieures à cette option — laisse le bulletin sur ses tableaux d'origine.
    EvaluationStructureDto? EvaluationStructure = null,

    // Tableau d'Honneur par matière (colonne « T.H ») : true dès que la moyenne de la matière atteint
    // le seuil de SubjectAppreciationScale.HonorMinAverage, transposé au barème du bulletin. La case
    // s'imprime « TH » ou reste vide — jamais « non », qui ferait lire un échec là où il n'y a qu'une
    // absence de distinction.
    //
    // Placé en fin de liste, loin de ses deux jumelles SubjectRanks/SubjectAppreciations, pour une
    // raison de langage et non de conception : un paramètre à valeur par défaut ne peut pas précéder
    // un paramètre obligatoire dans un record positionnel. Le défaut null (traité comme vide) est ce
    // qui laisse compiler les sites d'appel qui l'ignorent, à commencer par les tests des documents.
    IReadOnlyDictionary<Guid, bool>? SubjectHonors = null,

    // Module Coran/Franco-Arabe (SchoolSettings.IsCoranModuleEnabled) : bulletin bilingue quand vrai.
    // Faux par défaut — le rendu non-bilingue de toutes les écoles existantes ne bouge pas d'un point
    // (AGENTS.md règle #12).
    bool IsBilingualArabic = false,

    // Nom en arabe de chaque matière (Subject.NameAr), même convention que SubjectHonors/SubjectAppreciations
    // ci-dessus : un dictionnaire à part plutôt qu'un champ sur SubjectGradeDto, partagé avec la saisie
    // de notes qui n'a rien à voir avec le bulletin bilingue. Null/absent → aucune ligne n'imprime de
    // second nom, jamais une valeur inventée. Résolu UNIQUEMENT si IsBilingualArabic (évite une requête
    // inutile pour l'immense majorité des écoles).
    IReadOnlyDictionary<Guid, string?>? SubjectNamesAr = null,

    // Conseil de classe (Évolution N°7) — lus par le PV, jamais imprimés sur le bulletin (gabarit inchangé,
    // règle #12) : le sexe de l'élève (« M » | « F ») pour la ventilation Filles/Garçons, et la décision de fin
    // d'année PROPOSÉE d'après la moyenne annuelle et les seuils de l'école (CouncilRules.SuggestDecision).
    string? StudentGender = null,
    CouncilDecision? ProposedCouncilDecision = null,

    // Vrai si l'élève a une note de composition sur la période : « présent » au sens du PV.
    bool SatComposition = false);

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
        var directorSignature = await logoProvider.TryFetchAsync(dto.DirectorSignatureUrl, cancellationToken);
        var officialStamp = await logoProvider.TryFetchAsync(dto.OfficialStampUrl, cancellationToken);

        var fileName = $"Bulletin-{dto.Matricule}-{dto.TermLabel.Replace(' ', '-')}.pdf";
        return new ReportCardPdfResult(pdfGenerator.Generate(dto, logo, directorSignature, officialStamp), fileName);
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
            ?? throw new KeyNotFoundException($"Période {termId} introuvable dans votre établissement.");

        var classroom = await dbContext.Classrooms.AsNoTracking().FirstAsync(c => c.Id == student.ClassroomId, cancellationToken);
        var school = await dbContext.Schools.AsNoTracking().FirstAsync(s => s.Id == student.SchoolId, cancellationToken);
        var schoolYear = await dbContext.SchoolYears.AsNoTracking().FirstAsync(y => y.Id == term.SchoolYearId, cancellationToken);

        // Cachet/signature : sur SchoolSettings, pas School. Non trouvé (établissement pas encore
        // paramétré) → null des deux côtés, jamais une valeur inventée.
        var settings = await dbContext.SchoolSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == student.SchoolId, cancellationToken);

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

        // Barème du CYCLE de la classe de l'élève (Primaire /10, Collège & Lycée /20), et non un réglage
        // global d'école : le bulletin d'un CM2 affiche /10, celui d'une 3e /20, dans le même établissement.
        var gradingScale = await GradingScaleGuard.ResolveScaleForClassroomAsync(dbContext, student.ClassroomId, cancellationToken);

        // Échelle de mentions de l'ÉCOLE (configurable par le Directeur) : elle ne qualifie plus que la
        // moyenne générale et les lignes des grilles APC. Les deux colonnes par matière du tableau du
        // secondaire — Appréciations et T.H — relèvent désormais d'un barème distinct, voir ci-dessous.
        var mentionScale = await MentionScale.ResolveAsync(dbContext, cancellationToken);

        // Appréciation et Tableau d'Honneur par matière : le barème FIXE des bulletins sénégalais
        // (Faible → Très Bien, seuil T.H à 14/20), transposé au barème du bulletin — un CM2 noté /10
        // est jugé sur des seuils /10. Ce barème n'est PAS celui des mentions de l'école : voir
        // SubjectAppreciationScale, qui explique pourquoi les confondre imprimait « Passable » là où la
        // référence visuelle porte « Faible ».
        var subjectAppreciations = summary.Subjects.ToDictionary(
            s => s.SubjectId,
            s => SubjectAppreciationScale.For(s.Average, gradingScale));

        var subjectHonors = summary.Subjects.ToDictionary(
            s => s.SubjectId,
            s => SubjectAppreciationScale.QualifiesForHonors(s.Average, gradingScale));

        // Grille d'évaluation par compétences configurée pour le NIVEAU de la classe (domaines →
        // activités, barèmes propres, entêtes de colonnes). Null si ce niveau n'en déclare aucune : le
        // bulletin reprend alors ses tableaux d'origine. Les appréciations de ses lignes se calculent
        // sur le POURCENTAGE de réussite, avec les mentions de l'école telles qu'elles sont stockées
        // (/20) — pas les seuils transposés ci-dessus, qui supposent une note déjà sur gradingScale.
        // Les matières dispensées gardent leur ligne dans la grille, marquées (jamais retirées : le bulletin imprime
        // la grille ENTIÈRE).
        var exemptSubjectIds = (await ExemptionQueries.ForStudentAsync(
                dbContext, student.Id, term.SchoolYearId, cancellationToken))
            .Select(e => e.SubjectId)
            .ToHashSet();

        var evaluationStructure = await EvaluationStructureBuilder.BuildAsync(
            dbContext, classroom.Level, gradingScale, summary.Subjects, mentionScale, exemptSubjectIds, cancellationToken);

        var (absences, retards, totalAbsences) = await CountAttendanceAsync(
            student.Id, student.ClassroomId, term, cancellationToken);

        var remark = await dbContext.ReportCardRemarks.AsNoTracking()
            .FirstOrDefaultAsync(r => r.StudentId == student.Id && r.TermId == term.Id, cancellationToken);

        // Distinction de la rangée du bas — trois cas (voir DisciplinaryMention) :
        //  • None : le conseil a explicitement écarté toute distinction → aucune case, et la
        //    proposition automatique est neutralisée (c'est tout l'objet de cette valeur) ;
        //  • une autre valeur saisie : elle l'emporte TOUJOURS, y compris une sanction sur un
        //    excellent bulletin ;
        //  • rien de saisi (null) : la proposition déduite de la moyenne, et seulement dans sa moitié
        //    haute — DisciplinaryMentionPolicy ne propose jamais Blâme ni Avertissement.
        // Règles du conseil de l'école (Évolution N°7) : seuils des distinctions, note éliminatoire, décisions.
        var councilRules = CouncilRules.From(settings);

        var disciplinaryMention = remark?.DisciplinaryMention switch
        {
            DisciplinaryMention.None => (DisciplinaryMention?)null,
            { } chosen => chosen,
            null => DisciplinaryMentionPolicy.Suggest(
                summary.GeneralAverage, gradingScale, hasGrades: summary.TotalCoefficients > 0,
                councilRules, councilRules.HasEliminatoryGrade(summary.Subjects))
        };

        // Redoublement (feature F) : lu sur l'inscription NON annulée de l'élève pour l'exercice du
        // trimestre. Un élève sans inscription active sur cette année (cas limite d'un bulletin d'archive)
        // retombe sur false — case vide, jamais une valeur inventée.
        var isRepeating = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.StudentId == student.Id
                        && e.SchoolYearId == term.SchoolYearId
                        && e.Status != EnrollmentStatus.Cancelled)
            .Select(e => e.IsRepeating)
            .FirstOrDefaultAsync(cancellationToken);

        // Module Coran/Franco-Arabe : la requête des noms arabes n'a lieu QUE si le module est activé
        // pour l'école — inutile pour l'immense majorité des établissements.
        var isBilingualArabic = settings?.IsCoranModuleEnabled ?? false;
        IReadOnlyDictionary<Guid, string?>? subjectNamesAr = null;
        if (isBilingualArabic)
        {
            var subjectIds = summary.Subjects.Select(s => s.SubjectId).ToList();
            subjectNamesAr = await dbContext.Subjects.AsNoTracking()
                .Where(s => subjectIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.NameAr, cancellationToken);
        }

        var dto = new ReportCardDto(
            school.Name,
            school.LogoUrl,
            school.InspectionAcademie,
            school.InspectionEducationFormation,
            SchoolHeading.PrefixFor(classroom.Cycle),
            SchoolHeading.StripCyclePrefix(school.NomLycee),
            student.FullName,
            student.BirthDate,
            student.BirthPlace,
            classroom.Name,
            classroom.Cycle,
            student.Matricule,
            classmateIds.Count,
            isRepeating,
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
            disciplinaryMention,
            remark?.CouncilDecision,
            remark?.Observations,
            settings?.DirectorSignatureUrl,
            settings?.OfficialStampUrl,

            // Classe passerelle / accélérée (option). Les DEUX champs sortent du même moteur de
            // délibération que la clôture d'année (ClassroomPromotion) : le bulletin ne peut pas
            // annoncer un cursus que la promotion contredirait.
            ClassroomPromotion.AcceleratedPathLabel(classroom),
            ClassroomPromotion.ValidatedLevels(classroom, remark?.CouncilDecision),

            evaluationStructure,
            subjectHonors,
            isBilingualArabic,
            subjectNamesAr,
            student.Gender,
            councilRules.SuggestDecision(annualAverage, gradingScale),
            summary.Subjects.Any(s => s.Composition is not null));

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
