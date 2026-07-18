using FluentAssertions;
using SamaEcole.Application.Students.Queries.GetStudents;
using Xunit;

namespace SamaEcole.UnitTests.Students;

public class GetStudentsQueryValidatorTests
{
    private readonly GetStudentsQueryValidator _validator = new();

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(new GetStudentsQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        // Sans plafond, `?pageSize=1000000` laisse le client dicter la taille de la réponse et
        // transforme une simple liste en déni de service.
        var query = new GetStudentsQuery { PageSize = GetStudentsQueryValidator.MaxPageSize + 1 };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(new GetStudentsQuery { Page = page }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("M")]
    [InlineData("F")]
    public void Valid_Gender_Should_Pass(string? gender)
    {
        _validator.Validate(new GetStudentsQuery { Gender = gender }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Unknown_Gender_Should_Fail()
    {
        _validator.Validate(new GetStudentsQuery { Gender = "X" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Search_With_Html_Should_Fail()
    {
        // JGK-F01 — le texte de recherche est renvoyé à l'écran (« résultats pour… ») : branché sur
        // NoHtml comme les champs stockés (payloads exhaustifs : SafeTextValidationTests).
        var query = new GetStudentsQuery { Search = "<script>alert(1)</script>" };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }
}
