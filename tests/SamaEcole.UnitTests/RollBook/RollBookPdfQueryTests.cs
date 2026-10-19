using FluentAssertions;
using SamaEcole.Application.RollBook.Pdf;
using SamaEcole.Application.RollBook.Queries.GetRollBookPdf;
using Xunit;

namespace SamaEcole.UnitTests.RollBook;

/// <summary>Cahier de roulement imprimable (Bloc 5c) : validation de la requête, langues, découpage en mois, nom de fichier.</summary>
public class RollBookPdfQueryTests
{
    private readonly GetRollBookPdfQueryValidator _validator = new();
    private static readonly Guid Classe = Guid.NewGuid();

    [Fact]
    public void A_Month_Alone_Is_Valid()
        => _validator.Validate(new GetRollBookPdfQuery(Classe, "2026-11", null, null)).IsValid.Should().BeTrue();

    [Fact]
    public void A_Term_Alone_Is_Valid()
        => _validator.Validate(new GetRollBookPdfQuery(Classe, null, Guid.NewGuid(), "bi")).IsValid.Should().BeTrue();

    [Fact]
    public void Exactly_One_Of_Month_Or_Term_Is_Required()
    {
        _validator.Validate(new GetRollBookPdfQuery(Classe, null, null, null)).IsValid.Should().BeFalse("aucune période");
        _validator.Validate(new GetRollBookPdfQuery(Classe, "2026-11", Guid.NewGuid(), null)).IsValid.Should().BeFalse("deux périodes");
    }

    [Theory]
    [InlineData("novembre")]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    public void A_Bad_Month_Fails(string month)
        => _validator.Validate(new GetRollBookPdfQuery(Classe, month, null, null)).IsValid.Should().BeFalse();

    [Fact]
    public void A_Missing_Classroom_Fails()
        => _validator.Validate(new GetRollBookPdfQuery(Guid.Empty, "2026-11", null, null)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("fr", RollBookPdfLanguage.French)]
    [InlineData("ar", RollBookPdfLanguage.Arabic)]
    [InlineData("bi", RollBookPdfLanguage.Bilingual)]
    [InlineData("BI", RollBookPdfLanguage.Bilingual)]
    [InlineData(" ar ", RollBookPdfLanguage.Arabic)]
    public void Known_Languages_Parse(string value, RollBookPdfLanguage expected)
    {
        RollBookPdfLanguages.TryParse(value, out var language).Should().BeTrue();
        language.Should().Be(expected);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("")]
    [InlineData("arabic")]
    public void Unknown_Languages_Fail_Validation(string value)
    {
        RollBookPdfLanguages.TryParse(value, out _).Should().BeFalse();
        _validator.Validate(new GetRollBookPdfQuery(Classe, "2026-11", null, value)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Absent_Language_Is_Valid_And_Resolved_Later_From_The_School_Module()
        => _validator.Validate(new GetRollBookPdfQuery(Classe, "2026-11", null, null)).IsValid.Should().BeTrue();

    // -- Découpage d'une période en mois civils -----------------------------------------------------------

    [Fact]
    public void A_Term_Is_Cut_Into_Calendar_Months_Clipped_To_Its_Own_Dates()
    {
        var slices = GetRollBookPdfQueryHandler.MonthSlices(new DateOnly(2026, 10, 12), new DateOnly(2027, 1, 9)).ToList();

        slices.Should().Equal(
            (new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 31)),
            (new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            (new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)),
            (new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 9)));
    }

    [Fact]
    public void A_Single_Month_Is_One_Slice()
        => GetRollBookPdfQueryHandler.MonthSlices(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)).Should().ContainSingle()
            .Which.Should().Be((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));

    [Fact]
    public void A_Period_Within_One_Month_Is_Not_Widened_To_The_Whole_Month()
        => GetRollBookPdfQueryHandler.MonthSlices(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 20)).Should().ContainSingle()
            .Which.Should().Be((new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 20)));

    // -- Nom de fichier -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("CM2 A", "CM2-A")]
    [InlineData("6ème B", "6eme-B")]
    [InlineData("1er trimestre", "1er-trimestre")]
    [InlineData("Terminale S1/S2", "Terminale-S1-S2")]
    [InlineData("قسم", "classe")]
    [InlineData("", "classe")]
    public void A_File_Name_Part_Is_Ascii_Without_Spaces_Or_Accents(string value, string expected)
        => GetRollBookPdfQueryHandler.Slug(value).Should().Be(expected);
}
