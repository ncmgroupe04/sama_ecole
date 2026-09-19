using FluentAssertions;
using SamaEcole.Application.ClassJournal.Queries.GetClassJournal;
using Xunit;

namespace SamaEcole.UnitTests.ClassJournal;

/// <summary>Plafonnement de pagination (revue de code JGK-P04) — sans lui, une valeur invalide de
/// Page/PageSize fait échouer la requête EF Core en 500 au lieu d'un 422 propre.</summary>
public class GetClassJournalQueryValidatorTests
{
    private readonly GetClassJournalQueryValidator _validator = new();

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(new GetClassJournalQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        var query = new GetClassJournalQuery { PageSize = GetClassJournalQueryValidator.MaxPageSize + 1 };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(new GetClassJournalQuery { Page = page }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_PageSize_Should_Fail(int pageSize)
    {
        _validator.Validate(new GetClassJournalQuery { PageSize = pageSize }).IsValid.Should().BeFalse();
    }
}
