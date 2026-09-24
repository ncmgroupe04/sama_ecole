using System.Text;
using SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>Évolution N°1 — fiche de saisie papier (PDF vierge) : validation de la requête et rendu.</summary>
public class GradeSheetPdfTests
{
    private readonly GradeSheetPdfGenerator _generator = new();

    private static GradeSheetPdfDto Sheet(int students, decimal maxScore = 20) => new(
        "École Les Baobabs", "2026-2027", "1er trimestre", "6e A", "Mathématiques", "Devoir 1", maxScore,
        Enumerable.Range(1, students)
            .Select(i => new GradeSheetPdfStudent($"ELEV-2026-{i:0000}", $"Élève Numéro {i}"))
            .ToList());

    private static bool IsPdf(byte[] bytes) =>
        bytes.Length > 4 && Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    // --------------------------------------------------------------------------- Validateur

    [Fact]
    public void A_Complete_Query_Passes_Validation()
    {
        var query = new GetGradeSheetPdfQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvaluationType.Composition);

        new GetGradeSheetPdfQueryValidator().Validate(query).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Identifiers_Are_Refused()
    {
        var query = new GetGradeSheetPdfQuery(Guid.Empty, Guid.Empty, Guid.Empty, EvaluationType.Devoir1);

        var result = new GetGradeSheetPdfQueryValidator().Validate(query);

        result.Errors.Select(e => e.PropertyName).Should()
            .Contain([nameof(query.ClassroomId), nameof(query.SubjectId), nameof(query.TermId)]);
    }

    [Fact]
    public void An_Unknown_Evaluation_Type_Is_Refused()
    {
        var query = new GetGradeSheetPdfQuery(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (EvaluationType)99);

        new GetGradeSheetPdfQueryValidator().Validate(query).IsValid.Should().BeFalse();
    }

    // --------------------------------------------------------------------------- Rendu

    [Theory]
    [InlineData("1er semestre")]
    [InlineData("4e période")]
    public void A_Sheet_Of_A_Non_Trimester_Period_Renders_As_A_Pdf(string termLabel)
    {
        // Évolution N°2 : la ligne d'en-tête s'intitule « Période » et accepte tout libellé de période.
        var sheet = Sheet(30) with { TermLabel = termLabel };

        IsPdf(_generator.Generate(sheet, logo: null)).Should().BeTrue();
    }

    [Fact]
    public void A_Class_Renders_As_A_Pdf()
    {
        IsPdf(_generator.Generate(Sheet(30), logo: null)).Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Class_Still_Renders_A_Sheet()
    {
        IsPdf(_generator.Generate(Sheet(0), logo: null)).Should().BeTrue();
    }

    [Fact]
    public void A_Large_Class_Spans_More_Pages_Than_A_Small_One()
    {
        var small = _generator.Generate(Sheet(5), null);
        var large = _generator.Generate(Sheet(120), null);

        // Le rendu grossit avec les pages : sans pagination, 120 lignes seraient tronquées.
        large.Length.Should().BeGreaterThan(small.Length);
    }

    [Fact]
    public void A_Non_Integer_Scale_Renders_Without_Error()
    {
        IsPdf(_generator.Generate(Sheet(3, maxScore: 12.5m), null)).Should().BeTrue();
    }

    [Fact]
    public void A_Corrupt_Logo_Falls_Back_To_A_Sheet_Without_Logo()
    {
        // Le logo vient d'une URL saisie par le Directeur : un contenu illisible ne doit jamais
        // empêcher d'imprimer la fiche (PdfRenderGuard rejoue le rendu sans logo).
        var pdf = _generator.Generate(Sheet(10), logo: [0x00, 0x01, 0x02, 0x03]);

        IsPdf(pdf).Should().BeTrue();
    }
}
