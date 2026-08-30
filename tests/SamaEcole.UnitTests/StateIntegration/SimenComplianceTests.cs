using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Files;
using SamaEcole.Infrastructure.Services;
using Xunit;

namespace SamaEcole.UnitTests.StateIntegration;

/// <summary>
/// Garde-fous de conformité du module Intégration étatique (Volume 1 §23). Ces tests ne vérifient pas
/// des détails de mise en forme : ils verrouillent les invariants dont la violation produirait un
/// fichier officiel FAUX, ou une fuite de données, ou un mensonge affiché à l'utilisateur.
/// </summary>
public class SimenComplianceTests
{
    // ─────────────────────────────────────────────────────────  Export « Planète Ready »

    private static PlaneteStudentSyncDto SampleStudent(
        string? ien = "P01234726000175", bool provisional = true, string? guardian = "Awa Faye") =>
        new(
            IenNumber: ien,
            IsIenProvisional: provisional,
            Matricule: "ELEV-2026-0007",
            LastName: "Faye",
            FirstNames: "Mame Diarra Bousso",
            BirthDate: new DateOnly(2014, 3, 9),
            BirthPlace: "Saint-Louis",
            Gender: "F",
            ClassroomName: "CI A",
            Level: "CI",
            Cycle: "Primaire",
            SchoolYearLabel: "2026/2027",
            IsRepeating: false,
            GuardianName: guardian,
            GuardianPhone: null,
            NationalSchoolCode: "012347");

    private static PlaneteExportDto SampleExport(params PlaneteStudentSyncDto[] students) =>
        new(
            NationalSchoolCode: "012347",
            SchoolName: "École de test",
            InspectionAcademie: "Thiès",
            InspectionEducationFormation: "Mbour 1",
            SchoolDistrictCode: "MB-03",
            SchoolYearLabel: "2026/2027",
            GeneratedAt: new DateTimeOffset(2026, 8, 30, 9, 15, 0, TimeSpan.Zero),
            Students: students);

    [Fact]
    public void Csv_And_Json_Carry_Exactly_The_Same_Columns_In_The_Same_Order()
    {
        var serializer = new PlaneteExportSerializer();
        var export = SampleExport(SampleStudent());

        var csv = System.Text.Encoding.UTF8.GetString(
            serializer.Serialize(export, StateExportFormat.Csv).Content);
        var json = System.Text.Encoding.UTF8.GetString(
            serializer.Serialize(export, StateExportFormat.Json).Content);

        // En-tête CSV = première ligne, séparée par des points-virgules, BOM retiré.
        var csvColumns = csv.TrimStart('﻿')
            .Split('\n')[0].TrimEnd('\r')
            .Split(';');

        // Clés JSON de la première ligne élève, dans l'ordre d'apparition.
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var jsonColumns = doc.RootElement
            .GetProperty("eleves")[0]
            .EnumerateObject()
            .Select(p => p.Name)
            .ToArray();

        jsonColumns.Should().Equal(csvColumns,
            "un CSV et un JSON du même export doivent porter les mêmes colonnes dans le même ordre — "
            + "l'IEF rapproche les deux à la main");
    }

    [Fact]
    public void A_Missing_Value_Becomes_An_Empty_Cell_Never_A_Dash_Or_The_Word_Null()
    {
        var serializer = new PlaneteExportSerializer();
        // Tuteur non renseigné : la cellule doit rester vide.
        var export = SampleExport(SampleStudent(guardian: null, ien: null, provisional: false));

        var csv = System.Text.Encoding.UTF8.GetString(
                serializer.Serialize(export, StateExportFormat.Csv).Content)
            .TrimStart('﻿');

        var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        var header = lines[0].Split(';');
        var row = lines[1].Split(';');

        row[Array.IndexOf(header, "TUTEUR")].Should().BeEmpty();
        row[Array.IndexOf(header, "IEN")].Should().BeEmpty();

        csv.Should().NotContain(";-;").And.NotContain(";null;");
    }

    [Fact]
    public void The_Csv_Escapes_The_Delimiter_Per_Rfc_4180()
    {
        var serializer = new PlaneteExportSerializer();
        var tricky = SampleStudent() with { GuardianName = "Ndiaye; Fatou \"la grande\"" };

        var csv = System.Text.Encoding.UTF8.GetString(
            serializer.Serialize(SampleExport(tricky), StateExportFormat.Csv).Content);

        // Valeur entre guillemets, guillemets internes doublés — jamais un point-virgule nu qui
        // décalerait toutes les colonnes suivantes.
        csv.Should().Contain("\"Ndiaye; Fatou \"\"la grande\"\"\"");
    }

    [Fact]
    public void The_Csv_Starts_With_A_Utf8_Bom_So_Excel_Reads_Accents()
    {
        var serializer = new PlaneteExportSerializer();

        var bytes = serializer.Serialize(SampleExport(SampleStudent()), StateExportFormat.Csv).Content;

        bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public void Quality_Counters_Distinguish_Provisional_From_Missing_Ien()
    {
        var export = SampleExport(
            SampleStudent(ien: "SN123456789", provisional: false),           // officiel
            SampleStudent(ien: "P01234726000175", provisional: true),        // provisoire
            SampleStudent(ien: null, provisional: false));                   // absent

        export.StudentCount.Should().Be(3);
        export.ProvisionalIenCount.Should().Be(1);
        export.MissingIenCount.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────  Relais SIMEN (non ouvert)

    private static UnavailableSimenBridgeService Bridge() =>
        new(new StateIntegrationSettings(), NullLogger<UnavailableSimenBridgeService>.Instance);

    [Fact]
    public void The_Simen_Bridge_Is_Not_Configured_And_Never_Pretends_To_Transmit()
    {
        var bridge = Bridge();
        bridge.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Transmitting_Returns_A_Non_Accepted_Result_With_A_Reason_Never_Throws()
    {
        var bridge = Bridge();

        var result = await bridge.TransmitStudentsAsync(SampleExport(SampleStudent()), CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Outcome.Should().Be(SimenSyncOutcome.RelaisNonConfigure);
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Looking_Up_Official_Iens_Returns_An_Empty_List_Not_Rows_With_Null_Ien()
    {
        var bridge = Bridge();

        var results = await bridge.LookupOfficialIensAsync(
            [new SimenIenLookupRequest(Guid.NewGuid(), "Faye", "Awa", new DateOnly(2014, 1, 1), "Dakar")],
            CancellationToken.None);

        // « je n'ai pas cherché » ≠ « je n'ai rien trouvé » : une liste vide, jamais N résultats vides.
        results.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────  Forme de l'IEN provisoire

    [Fact]
    public void A_Composed_Provisional_Ien_Is_Marked_Well_Formed_And_Recognisable()
    {
        var ien = IenNumberFormat.ComposeProvisional("IA-01/2347", academicYear: 2026, sequence: 175);

        ien.Should().StartWith("P");
        ien.Should().HaveLength(IenNumberFormat.ProvisionalIenLength);
        ien[1..].Should().MatchRegex("^[0-9]+$");
        IenNumberFormat.IsProvisional(ien).Should().BeTrue();
        IenNumberFormat.IsWellFormed(ien).Should().BeTrue();
    }

    [Theory]
    [InlineData("P0123472600017X")]   // notre préfixe, mais un caractère non numérique
    [InlineData("P0123472600017")]    // notre préfixe, mais trop court
    [InlineData("P0123472600017999")] // notre préfixe, mais trop long
    [InlineData("ab")]                // trop court pour être quoi que ce soit
    [InlineData("with spaces inside")]// caractères non alphanumériques
    [InlineData("   ")]
    [InlineData("")]
    public void Malformed_Numbers_Are_Rejected(string candidate)
    {
        IenNumberFormat.IsWellFormed(candidate).Should().BeFalse();
    }

    [Fact]
    public void A_Single_Digit_Typo_In_A_Provisional_Ien_Breaks_The_Check_Digit()
    {
        var good = IenNumberFormat.ComposeProvisional("012347", 2026, 175);

        // On altère un chiffre du corps (position 5) : la clé Luhn ne doit plus correspondre.
        var chars = good.ToCharArray();
        chars[5] = chars[5] == '9' ? '0' : (char)(chars[5] + 1);
        var typo = new string(chars);

        IenNumberFormat.IsWellFormed(typo).Should().BeFalse();
    }

    [Fact]
    public void A_Plausible_Official_Number_Passes_Form_Check_Without_Claiming_Authenticity()
    {
        // Un IEN officiel ne suit pas notre format ; on accepte sa FORME (garde-fou anti-frappe),
        // on ne certifie rien de plus — voir IenNumberFormat.IsWellFormed.
        IenNumberFormat.IsWellFormed("SEN2026DK00459123").Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────  Coordonnées GPS de l'école

    [Fact]
    public void Gps_Coordinates_Are_Rendered_With_An_Invariant_Decimal_Point()
    {
        var school = new School
        {
            Name = "École de test",
            GpsLatitude = 14.692800m,
            GpsLongitude = -17.446700m
        };

        // Jamais une virgule décimale : « 14,6928, -17,4467 » serait illisible pour l'importeur.
        school.GpsCoordinates.Should().Be("14.692800, -17.446700");
    }

    [Fact]
    public void Gps_Coordinates_Are_Null_When_One_Half_Is_Missing()
    {
        var school = new School { Name = "École", GpsLatitude = 14.6928m, GpsLongitude = null };

        school.GpsCoordinates.Should().BeNull("une latitude sans longitude ne localise rien");
    }

    // ─────────────────────────────────────────────────────────  Échelle d'acquisition APC

    [Theory]
    [InlineData(0, 20, SkillAcquisitionLevel.NonAcquis)]
    [InlineData(9, 20, SkillAcquisitionLevel.EnCoursAcquisition)]   // 45 %
    [InlineData(14, 20, SkillAcquisitionLevel.Acquis)]              // 70 %
    [InlineData(19, 20, SkillAcquisitionLevel.Expert)]              // 95 %
    public void Skill_Acquisition_Follows_The_Documented_Thresholds(
        double score, double maxScore, SkillAcquisitionLevel expected)
    {
        SkillAcquisition.FromScore((decimal)score, (decimal)maxScore).Should().Be(expected);
    }

    [Fact]
    public void Skill_Acquisition_Is_Null_When_There_Is_No_Score()
    {
        SkillAcquisition.FromScore(null, 20m).Should().BeNull(
            "une case non évaluée n'est jamais « non acquis » — ce serait un jugement que personne n'a porté");
    }

    [Fact]
    public void Skill_Acquisition_Is_Null_When_The_Grid_Has_A_Zero_Max_Score()
    {
        SkillAcquisition.FromScore(12m, 0m).Should().BeNull("un barème nul est une grille mal configurée, pas un échec");
    }
}
