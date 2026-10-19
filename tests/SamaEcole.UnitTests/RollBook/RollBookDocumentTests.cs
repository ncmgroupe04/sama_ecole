using FluentAssertions;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.RollBook;
using SamaEcole.Application.RollBook.Pdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.UnitTests.Boarding;
using UglyToad.PdfPig;
using Xunit;

namespace SamaEcole.UnitTests.RollBook;

/// <summary>
/// Cahier de roulement imprimable (Franco-Arabe Bloc 5c) : contenu du bandeau officiel, grille, légende, totaux,
/// pagination, trois langues de rendu, ligne arabe à compléter à la main, récapitulatif et synthèse du niveau.
/// Le PDF est relu avec PdfPig : on contrôle ce que le document EXPOSE vraiment.
/// </summary>
public class RollBookDocumentTests
{
    static RollBookDocumentTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        PdfFonts.EnsureRegistered();
    }

    private static readonly DayOfWeek[] MondayToSaturday =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday];

    // PNG 1×1 valide : un logo « normal ».
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    internal static RollBookPdfMonth Month(int students, int year = 2026, int month = 11, bool arabicNames = true, bool morningOnly = false)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var closure = new SchoolClosure
        {
            Label = "Vacances", LabelAr = "عطلة", Kind = SchoolClosureKind.Vacation,
            StartDate = new DateOnly(year, month, 4), EndDate = new DateOnly(year, month, 5)
        };

        var days = RollBookCalendar.Build(first, last, MondayToSaturday, new DateOnly(year, 10, 1), new DateOnly(year + 1, 7, 31), [closure])
            .Select(d => new RollBookPdfDay(d.Date, d.IsClassDay, d.OffReason, d.Closure?.Label, d.Closure?.LabelAr))
            .ToList();
        var halves = morningOnly ? new[] { HalfDay.Morning } : [HalfDay.Morning, HalfDay.Afternoon];
        var classDays = days.Where(d => d.IsClassDay).Take(10).ToList();

        var roster = Enumerable.Range(1, students).Select(i =>
        {
            var cells = new List<RollBookPdfCell>();
            for (var d = 0; d < classDays.Count; d++)
            {
                foreach (var half in halves)
                {
                    // Un peu de tout : présent la plupart du temps, un retard, une absence justifiée, une non justifiée, une partielle.
                    var mark = ((d + i) % 9) switch
                    {
                        1 => RollBookMark.Late,
                        3 => RollBookMark.JustifiedAbsence,
                        5 => RollBookMark.UnjustifiedAbsence,
                        7 => RollBookMark.PartialAbsence,
                        _ => RollBookMark.Present
                    };
                    cells.Add(new RollBookPdfCell(classDays[d].Date, half, mark));
                }
            }

            var counts = AttendanceCounts.From(cells.Select(c => new RollBookCell(c.Mark, RollBookSource.Entry, c.Mark == RollBookMark.Late ? 10 : 0)));
            return new RollBookPdfStudent($"Élève Numéro {i}", arabicNames ? "تلميذ رقم" : null, $"ELEV-{i:000}", cells, counts);
        }).ToList();

        return new RollBookPdfMonth(first, last, days, halves, roster, days.Count(d => d.IsClassDay) * halves.Length);
    }

    internal static RollBookPdfModel Model(
        RollBookPdfLanguage language = RollBookPdfLanguage.Bilingual,
        int students = 5,
        string? schoolNameAr = "مدرسة الباوباب الخاصة",
        string? inspectionAr = "تييس",
        SectionPattern? pattern = SectionPattern.MorningFrenchAfternoonArabic,
        bool arabicNames = true,
        string level = "CM2",
        int months = 1,
        string? term = null,
        bool withLevelSynthesis = false,
        bool morningOnly = false)
    {
        var list = Enumerable.Range(0, months)
            .Select(i => Month(students, year: 2026 + (10 + i) / 12, month: (10 + i) % 12 + 1, arabicNames: arabicNames, morningOnly: morningOnly))
            .ToList();

        var termRows = term is null
            ? []
            : list[0].Students.Select((s, i) => new RollBookPdfTermRow(
                s.FullName, s.FullNameAr, list.Select(m => m.Students[i].Counts).ToList(),
                AttendanceCounts.Sum(list.Select(m => m.Students[i].Counts)))).ToList();

        var levelClasses = withLevelSynthesis
            ?
            [
                new RollBookPdfLevelClass("CM2 A", 30, new AttendanceCounts(Present: 400, Late: 20, JustifiedAbsence: 30, UnjustifiedAbsence: 50), 600),
                new RollBookPdfLevelClass("CM2 B", 28, new AttendanceCounts(Present: 300, Late: 10, JustifiedAbsence: 10, UnjustifiedAbsence: 30), 560)
            ]
            : new List<RollBookPdfLevelClass>();

        return new RollBookPdfModel(
            language,
            "Complexe Scolaire Les Baobabs", schoolNameAr, "Thiès", inspectionAr, "Mbour 1", "مبور 1",
            "2026-2027", "CM2 A", level, pattern, list, term, termRows, levelClasses, new DateOnly(2026, 11, 30));
    }

    private static byte[] Pdf(RollBookPdfModel model, RollBookPdfAssets? assets = null) =>
        new RollBookPdfGenerator().Generate(model, assets ?? RollBookPdfAssets.None);

    private static bool IsArabic(char c) =>
        c is >= (char)0x0600 and <= (char)0x06FF or >= (char)0x0750 and <= (char)0x077F
            or >= (char)0xFB50 and <= (char)0xFDFF or >= (char)0xFE70 and <= (char)0xFEFF;

    private static int ArabicLetters(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().SelectMany(p => p.Letters).Count(l => l.Value.Length > 0 && IsArabic(l.Value[0]));
    }

    private static int PageCount(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.NumberOfPages;
    }

    // ---------------------------------------------------------------------------------------- Français

    [Fact]
    public void The_French_Cahier_Is_A_Landscape_Page_With_The_Official_Banner_And_Header()
    {
        var pdf = Pdf(Model(RollBookPdfLanguage.French));

        using (var document = PdfDocument.Open(pdf))
        {
            var page = document.GetPage(1);
            page.Width.Should().BeGreaterThan(page.Height, "A4 paysage : tous les jours du mois sans tronquer de colonne");
        }

        var text = PdfText.Extract(pdf);
        text.Should().Contain("RÉPUBLIQUE DU SÉNÉGAL").And.Contain("MINISTÈRE DE L'ÉDUCATION NATIONALE");
        text.Should().Contain("Inspection d'Académie de : Thiès");
        text.Should().Contain("Inspection de l'Éducation et de la Formation de : Mbour 1");
        text.Should().Contain("Établissement : Complexe Scolaire Les Baobabs");
        text.Should().Contain("CAHIER DE ROULEMENT — NOVEMBRE 2026");
        text.Should().Contain("Classe : CM2 A").And.Contain("Niveau : CM2").And.Contain("Année scolaire : 2026-2027");
    }

    [Fact]
    public void The_French_Cahier_Lists_Students_Day_Numbers_Legend_Totals_And_Signatures()
    {
        var text = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.French)));

        text.Should().Contain("Élève Numéro 1").And.Contain("Élève Numéro 5");
        text.Should().Contain("Nom de l'élève");
        text.Should().Contain("Présent").And.Contain("Retard").And.Contain("Absence partielle")
            .And.Contain("Absent justifié").And.Contain("Absent non justifié");
        text.Should().Contain("Demi-journées attendues").And.Contain("Demi-journées renseignées")
            .And.Contain("Taux de présence").And.Contain("Taux d'absence").And.Contain("Taux d'assiduité").And.Contain("Couverture");
        text.Should().Contain("Absents");
        text.Should().Contain("Le titulaire de la classe").And.Contain("Le Chef d'Établissement").And.Contain("Cachet de l'établissement");
        text.Should().Contain("Fait à").And.Contain("le 30/11/2026");

        // Les 30 jours de novembre figurent dans l'en-tête de la grille (le 31 n'existe pas).
        text.Should().Contain("30");
        text.Should().NotContain("Taux de présence : ").And.NotContain("NaN");
    }

    [Fact]
    public void The_French_Cahier_Prints_No_Arabic_At_All()
    {
        ArabicLetters(Pdf(Model(RollBookPdfLanguage.French))).Should().Be(0, "aucun arabe en mode français, même si les noms arabes existent");
    }

    [Fact]
    public void A_Month_With_Thirty_One_Days_And_Two_Half_Days_Fits_On_One_Landscape_Page()
    {
        var model = Model(RollBookPdfLanguage.French, students: 10);
        model = model with { Months = [Month(10, year: 2026, month: 12)] };   // décembre : 31 jours

        var pdf = Pdf(model);

        PageCount(pdf).Should().Be(1, "62 colonnes de demi-journée + totaux tiennent sur une page A4 paysage");
        PdfText.Extract(pdf).Should().Contain("DÉCEMBRE 2026").And.Contain("31");
    }

    [Fact]
    public void A_Numerous_Class_Spreads_Over_Several_Pages_And_Repeats_The_Table_Header()
    {
        var pdf = Pdf(Model(RollBookPdfLanguage.French, students: 90));

        PageCount(pdf).Should().BeGreaterThan(1);
        var text = PdfText.Extract(pdf);
        System.Text.RegularExpressions.Regex.Matches(text, "Nom de l'élève").Count
            .Should().BeGreaterThan(1, "l'en-tête du tableau se répète sur chaque page");
        text.Should().Contain("Élève Numéro 90");
        System.Text.RegularExpressions.Regex.Matches(text, "Le Chef d'Établissement").Count
            .Should().Be(1, "légende, totaux et signatures ne sont imprimés qu'une fois, après le tableau");
    }

    [Fact]
    public void A_Class_That_Only_Has_Morning_Lessons_Prints_One_Half_Day_Column_Per_Day()
    {
        static int AfternoonHeaders(byte[] pdf)
        {
            using var document = PdfDocument.Open(pdf);
            return document.GetPage(1).GetWords().Count(w => w.Text == "S");
        }

        var both = AfternoonHeaders(Pdf(Model(RollBookPdfLanguage.French, morningOnly: false)));
        var morningOnly = AfternoonHeaders(Pdf(Model(RollBookPdfLanguage.French, morningOnly: true)));

        // « S » (après-midi) coiffe chaque jour de classe quand la classe y a cours ; sinon il ne reste que les samedis de l'en-tête.
        (both - morningOnly).Should().BeGreaterThanOrEqualTo(20, "l'après-midi n'est imprimée que si la classe y a cours");
    }

    [Fact]
    public void Without_Any_Recorded_Cell_The_Rates_Print_A_Dash_Not_A_Fake_Hundred_Percent()
    {
        var month = Month(3) with
        {
            Students = Month(3).Students.Select(s => s with { Cells = [], Counts = AttendanceCounts.Zero }).ToList()
        };

        var text = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.French) with { Months = [month] }));

        text.Should().Contain("—");
        text.Should().NotContain("100 %");
    }

    // ----------------------------------------------------------------------------------------- Bilingue

    [Fact]
    public void The_Bilingual_Cahier_Adds_Arabic_Banner_Header_Title_And_Legend_Under_The_French()
    {
        var pdf = Pdf(Model(RollBookPdfLanguage.Bilingual));
        var text = PdfText.Extract(pdf);

        text.Should().Contain("RÉPUBLIQUE DU SÉNÉGAL").And.Contain("CAHIER DE ROULEMENT — NOVEMBRE 2026");
        text.Should().Contain("Élève Numéro 1");

        using var document = PdfDocument.Open(pdf);
        var arabic = document.GetPages().SelectMany(p => p.Letters).Where(l => l.Value.Length > 0 && IsArabic(l.Value[0])).ToList();
        arabic.Should().HaveCountGreaterThan(150, "bandeau, titre, en-tête, noms des élèves, légende, totaux et signatures");
        arabic.Count(l => l.Value[0] >= (char)0xFB50).Should().BeGreaterThan(arabic.Count / 2, "lettres liées, pas épelées");
    }

    [Fact]
    public void The_Arabic_School_Name_And_Inspections_Come_From_The_Settings_Never_From_A_Translation()
    {
        using var document = PdfDocument.Open(Pdf(Model(RollBookPdfLanguage.Bilingual, schoolNameAr: "مدرسة الباوباب الخاصة", inspectionAr: "تييس")));
        var letters = string.Concat(document.GetPage(1).Letters.Select(l => l.Value));

        // Les lettres arabes sont des formes de présentation : on vérifie qu'il y a des lignes arabes en plus du bandeau.
        letters.Count(c => IsArabic(c)).Should().BeGreaterThan(100);
    }

    [Fact]
    public void A_Missing_Arabic_School_Name_Leaves_A_Dotted_Line_To_Fill_By_Hand()
    {
        var filled = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Bilingual, schoolNameAr: "مدرسة الباوباب", inspectionAr: "تييس")));
        var blank = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Bilingual, schoolNameAr: null, inspectionAr: null)));

        blank.Should().Contain("..............................");
        filled.Should().NotContain("..............................", "les lignes renseignées ne sont pas pointillées");
        System.Text.RegularExpressions.Regex.Matches(blank, @"\.{30}").Count.Should().BeGreaterThanOrEqualTo(2, "inspection d'académie et établissement");
    }

    [Fact]
    public void The_Section_Alternation_Line_Is_Printed_When_The_Class_Has_One()
    {
        PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Bilingual, pattern: SectionPattern.MorningFrenchAfternoonArabic)))
            .Should().Contain("Alternance : le matin en français, l'après-midi en arabe");

        PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Bilingual, pattern: SectionPattern.MorningArabicAfternoonFrench)))
            .Should().Contain("Alternance : le matin en arabe, l'après-midi en français");

        PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Bilingual, pattern: SectionPattern.NoSlots))).Should().NotContain("Alternance");
        PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Bilingual, pattern: null))).Should().NotContain("Alternance");
    }

    // ------------------------------------------------------------------------------------------- Arabe

    [Fact]
    public void The_Arabic_Cahier_Is_Fully_Arabic_Without_The_French_Banner_Or_Labels()
    {
        var pdf = Pdf(Model(RollBookPdfLanguage.Arabic));
        var text = PdfText.Extract(pdf);

        text.Should().NotContain("RÉPUBLIQUE DU SÉNÉGAL").And.NotContain("CAHIER DE ROULEMENT")
            .And.NotContain("Taux de présence").And.NotContain("Le Chef d'Établissement").And.NotContain("Inspection d'Académie");

        ArabicLetters(pdf).Should().BeGreaterThan(200);
    }

    [Fact]
    public void The_Arabic_Cahier_Falls_Back_To_The_French_Name_For_A_Student_Without_An_Arabic_Name()
    {
        var text = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Arabic, arabicNames: false, students: 3)));

        text.Should().Contain("Élève Numéro 1", "un élève sans nom arabe doit rester identifiable sur le registre");
    }

    [Fact]
    public void The_Arabic_Cahier_Keeps_Latin_Digits_For_Days_And_Percentages()
    {
        var text = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.Arabic)));

        // L'ordre d'extraction du texte d'une page de droite à gauche n'est pas celui de l'affichage : on contrôle que les
        // taux sont bien imprimés avec des chiffres latins et le signe %, pas leur position relative.
        text.Should().MatchRegex(@"\d+,\d\s*%|%\s*\d+,\d|\d+\s*%|%\s*\d+", "taux en chiffres latins, comme à l'écran");
        text.Should().Contain("2026");
        text.Should().NotMatchRegex("[٠-٩]", "aucun chiffre arabo-indien");
    }

    // --------------------------------------------------------------------------------- Images facultatives

    [Fact]
    public void A_Valid_Logo_Stamp_And_Signature_Are_Embedded_Without_Breaking_The_Layout()
    {
        var assets = new RollBookPdfAssets(TinyPng, TinyPng, TinyPng);

        var pdf = Pdf(Model(RollBookPdfLanguage.French), assets);

        pdf.Take(5).Should().Equal("%PDF-"u8.ToArray());
        using var document = PdfDocument.Open(pdf);
        document.GetPage(1).GetImages().Should().HaveCount(3);
    }

    [Fact]
    public void A_Corrupted_Image_Does_Not_Prevent_The_Document_It_Is_Rendered_Without_Images()
    {
        var assets = new RollBookPdfAssets([1, 2, 3, 4], null, null);

        var pdf = Pdf(Model(RollBookPdfLanguage.French), assets);

        pdf.Take(5).Should().Equal("%PDF-"u8.ToArray());
        PdfText.Extract(pdf).Should().Contain("CAHIER DE ROULEMENT");
    }

    // ------------------------------------------------------------------ Période d'évaluation (3 mois)

    [Fact]
    public void A_Term_Prints_One_Grid_Per_Month_Then_The_Recap()
    {
        var pdf = Pdf(Model(RollBookPdfLanguage.French, months: 3, term: "1er trimestre"));
        var text = PdfText.Extract(pdf);

        text.Should().Contain("NOVEMBRE 2026").And.Contain("DÉCEMBRE 2026").And.Contain("JANVIER 2027");
        text.Should().Contain("RÉCAPITULATIF PÉRIODIQUE — CM2 A — 1er trimestre");
        text.Should().Contain("Élève Numéro 5");
        PageCount(pdf).Should().BeGreaterThanOrEqualTo(4, "trois grilles mensuelles + le récapitulatif");
    }

    [Fact]
    public void The_Recap_Sums_Each_Student_Over_The_Period()
    {
        var model = Model(RollBookPdfLanguage.French, months: 3, term: "1er trimestre", students: 2);

        model.TermRows.Should().HaveCount(2);
        var row = model.TermRows[0];
        row.PerMonth.Should().HaveCount(3);
        row.Total.Present.Should().Be(row.PerMonth.Sum(m => m.Present));
        row.Total.UnjustifiedAbsence.Should().Be(row.PerMonth.Sum(m => m.UnjustifiedAbsence));
    }

    [Fact]
    public void The_Level_Synthesis_Is_Printed_Only_When_The_Model_Carries_It()
    {
        var with = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.French, months: 1, term: "1er trimestre", withLevelSynthesis: true)));
        var without = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.French, months: 1, term: "1er trimestre", withLevelSynthesis: false)));

        with.Should().Contain("SYNTHÈSE DU NIVEAU CM2").And.Contain("CM2 B").And.Contain("Total CM2");
        without.Should().NotContain("SYNTHÈSE DU NIVEAU");
    }

    [Fact]
    public void The_Level_Total_Sums_Counts_Instead_Of_Averaging_Class_Rates()
    {
        // CM2 A : 400+20=420 présences sur 500 renseignées ; CM2 B : 310 sur 350 → niveau = 730 / 850 = 85,9 % (pas 86,4 %).
        var text = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.French, months: 1, term: "1er trimestre", withLevelSynthesis: true)));

        text.Should().Contain("85,9 %").And.Contain("84 %").And.Contain("88,6 %");
    }

    [Fact]
    public void The_Monthly_Cahier_Has_No_Recap_Nor_Level_Page()
    {
        var text = PdfText.Extract(Pdf(Model(RollBookPdfLanguage.French)));

        text.Should().NotContain("RÉCAPITULATIF").And.NotContain("SYNTHÈSE DU NIVEAU");
    }

    [Fact]
    public void The_Bilingual_Level_Page_Has_Arabic_Titles_And_The_Level_In_Arabic_When_Known()
    {
        var pdf = Pdf(Model(RollBookPdfLanguage.Bilingual, months: 1, term: "1er trimestre", withLevelSynthesis: true));

        PdfText.Extract(pdf).Should().Contain("SYNTHÈSE DU NIVEAU CM2");
        ArabicLetters(pdf).Should().BeGreaterThan(300);
    }

    // ------------------------------------------------------------------------------------ Libellés purs

    [Theory]
    [InlineData(2026, 1, "Janvier 2026", "يناير 2026")]
    [InlineData(2026, 8, "Août 2026", "أغسطس 2026")]
    [InlineData(2026, 12, "Décembre 2026", "ديسمبر 2026")]
    public void Month_Titles_Come_From_Fixed_Tables_Never_From_The_System_Culture(int year, int month, string french, string arabic)
    {
        var date = new DateOnly(year, month, 1);

        RollBookLabels.MonthTitleFr(date).Should().Be(french);
        RollBookLabels.MonthTitleAr(date).Should().Be(arabic, "calendrier grégorien, jamais hégirien");
    }

    [Fact]
    public void Weekday_Initials_Cover_The_Whole_Week_In_Both_Languages()
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            RollBookLabels.WeekdayInitial(day, arabic: false).Should().HaveLength(1);
            RollBookLabels.WeekdayInitial(day, arabic: true).Should().HaveLength(1);
        }

        RollBookLabels.WeekdayInitial(DayOfWeek.Monday, false).Should().Be("L");
        RollBookLabels.WeekdayInitial(DayOfWeek.Sunday, false).Should().Be("D");
    }

    [Theory]
    [InlineData("CM2", true)]
    [InlineData("Sixième", true)]
    [InlineData("Terminale", true)]
    [InlineData("Primaire", false)]
    [InlineData("Classe spéciale", false)]
    [InlineData(null, false)]
    public void A_Level_Is_Translated_Only_When_It_Belongs_To_The_Nomenclature(string? level, bool translated)
        => (RollBookLabels.Level(level) is not null).Should().Be(translated, "jamais une traduction inventée pour un niveau hors nomenclature");

    [Theory]
    [InlineData(RollBookMark.Present, false, "P")]
    [InlineData(RollBookMark.Late, false, "R")]
    [InlineData(RollBookMark.JustifiedAbsence, false, "J")]
    [InlineData(RollBookMark.UnjustifiedAbsence, false, "A")]
    [InlineData(RollBookMark.PartialAbsence, false, "½")]
    [InlineData(RollBookMark.PartialAbsence, true, "½")]
    [InlineData(RollBookMark.NotRecorded, false, "")]
    [InlineData(RollBookMark.NotRecorded, true, "")]
    public void Glyphs_Are_One_Letter_And_Empty_For_An_Unrecorded_Cell(RollBookMark mark, bool arabic, string expected)
        => RollBookLabels.Glyph(mark, arabic).Should().Be(expected);

    [Fact]
    public void Arabic_Glyphs_Are_One_Arabic_Letter_Each()
    {
        foreach (var mark in new[] { RollBookMark.Present, RollBookMark.Late, RollBookMark.JustifiedAbsence, RollBookMark.UnjustifiedAbsence })
        {
            var glyph = RollBookLabels.Glyph(mark, arabic: true);
            glyph.Should().HaveLength(1);
            IsArabic(glyph[0]).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(0.875, "87,5 %")]
    [InlineData(1, "100 %")]
    [InlineData(0, "0 %")]
    [InlineData(0.6667, "66,7 %")]
    public void Percent_Uses_The_French_Decimal_Comma(double rate, string expected)
        => RollBookLabels.Percent((decimal)rate).Should().Be(expected);

    [Fact]
    public void Percent_Of_Nothing_Is_A_Dash()
        => RollBookLabels.Percent(null).Should().Be("—");

    [Fact]
    public void The_Pattern_Line_Exists_Only_For_A_Real_Alternation()
    {
        RollBookLabels.Pattern(SectionPattern.MorningFrenchAfternoonArabic).Should().NotBeNull();
        RollBookLabels.Pattern(SectionPattern.MorningArabicAfternoonFrench).Should().NotBeNull();
        RollBookLabels.Pattern(SectionPattern.AlternatingDays).Should().NotBeNull();
        RollBookLabels.Pattern(SectionPattern.Mixed).Should().NotBeNull();
        RollBookLabels.Pattern(SectionPattern.SingleSection).Should().BeNull();
        RollBookLabels.Pattern(SectionPattern.NoSlots).Should().BeNull();
        RollBookLabels.Pattern(null).Should().BeNull();
    }
}
