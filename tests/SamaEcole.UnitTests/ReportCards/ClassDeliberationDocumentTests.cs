using FluentAssertions;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>
/// PV de délibération (A4 portrait) — sans AUCUNE couverture avant ce correctif, alors que le document
/// est pleinement câblé au contrôleur et au front. Contrairement au bulletin A5, une classe complète
/// (jusqu'à ~40 élèves) peut légitimement déborder sur plusieurs pages A4 : pas de contrainte
/// une-seule-page ici, seulement la validité du PDF produit.
/// </summary>
public class ClassDeliberationDocumentTests
{
    static ClassDeliberationDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static ReportCardDto BuildReportCard(
        string fullName,
        decimal generalAverage,
        int generalRank,
        string? acceleratedPathLabel = null,
        IReadOnlyList<string>? validatedLevels = null) => new(
        SchoolName: "École de test",
        SchoolLogoUrl: null,
        InspectionAcademie: "Thies",
        InspectionEducationFormation: "Mbour 1",
        HeadingPrefix: "ÉCOLE ÉLÉMENTAIRE DE",
        HeadingName: "Popenguine",
        StudentFullName: fullName,
        BirthDate: new DateOnly(2015, 3, 14),
        BirthPlace: "Dakar",
        ClassroomName: "CM2",
        Cycle: CycleType.Primaire,
        Matricule: "ELEV-2026-0001",
        ClassSize: 3,
        IsRepeating: false,
        SchoolYearLabel: "2026-2027",
        TermLabel: "1er trimestre",
        GradingScale: 10,
        Subjects: [],
        TotalCoefficients: 4m,
        TotalPoints: generalAverage * 4m,
        GeneralAverage: generalAverage,
        GeneralRank: generalRank,
        SubjectRanks: new Dictionary<Guid, int>(),
        SubjectAppreciations: new Dictionary<Guid, string?>(),
        Mention: generalAverage >= 5m ? "Bien" : null,
        Absences: null,
        Retards: null,
        TotalAbsences: null,
        TermRecaps: [],
        AnnualAverage: null,
        AnnualRank: null,
        DisciplinaryMention: null,
        CouncilDecision: generalAverage >= 5m ? CouncilDecision.Admitted : CouncilDecision.AllowedToRepeat,
        CouncilObservations: null,
        AcceleratedPathLabel: acceleratedPathLabel,
        ValidatedLevels: validatedLevels);

    private static void ShouldBeAValidPdf(byte[] pdf)
    {
        pdf.Should().NotBeNullOrEmpty();
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Generate_Produces_A_Valid_Pdf_For_A_Typical_Class()
    {
        var reportCards = new[]
        {
            BuildReportCard("Zorro Diallo", 9m, 1),
            BuildReportCard("Awa Sow", 4m, 2)
        };

        var bytes = new ClassDeliberationDocument(reportCards, logo: null).GeneratePdf();

        ShouldBeAValidPdf(bytes);
    }

    /// <summary>
    /// Une classe complète (jusqu'à l'effectif maximal d'une Classroom, 40 par Volume 1) doit toujours
    /// produire un PV valide, quel que soit le nombre de pages A4 nécessaires — pas de contrainte
    /// une-seule-page comme sur le bulletin individuel A5.
    /// </summary>
    [Fact]
    public void Generate_Handles_A_Full_Forty_Student_Classroom_Without_Error()
    {
        var reportCards = Enumerable.Range(1, 40)
            .Select(i => BuildReportCard($"Élève {i:D2}", 5m + i % 5, i))
            .ToArray();

        var bytes = new ClassDeliberationDocument(reportCards, logo: null).GeneratePdf();

        ShouldBeAValidPdf(bytes);
    }

    /// <summary>
    /// PV d'une classe PASSERELLE : l'en-tête porte la mention du cursus et la colonne « Décision du
    /// Conseil » nomme les DEUX niveaux acquis par chaque élève admis — c'est ce PV qui fait foi du
    /// passage, et rien d'autre dans l'archive de l'école n'attesterait du niveau sauté.
    /// </summary>
    [Fact]
    public void Generate_Produces_A_Valid_Pv_For_An_Accelerated_Bridge_Class()
    {
        var reportCards = new[]
        {
            BuildReportCard("Zorro Diallo", 9m, 1, "Cursus Accéléré Passerelle — CI → CP", ["CI", "CP"]),
            // Élève NON admis de la même classe : ValidatedLevels est vide, la cellule ne doit annoncer
            // aucun niveau acquis alors même que la classe, elle, est bien une passerelle.
            BuildReportCard("Awa Sow", 4m, 2, "Cursus Accéléré Passerelle — CI → CP", [])
        };

        var bytes = new ClassDeliberationDocument(reportCards, logo: null).GeneratePdf();

        ShouldBeAValidPdf(bytes);
    }

    /// <summary>
    /// ComposeHeader/ComposeContent retombent tôt sur une liste vide (jamais appelée en pratique — le
    /// handler lève BusinessRuleException avant — mais le document lui-même doit rester tolérant à ce
    /// cas plutôt que de planter si jamais invoqué directement).
    /// </summary>
    [Fact]
    public void Generate_Does_Not_Throw_On_An_Empty_Report_Card_List()
    {
        var act = () => new ClassDeliberationDocument([], logo: null).GeneratePdf();

        act.Should().NotThrow();
    }
}
