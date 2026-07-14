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
}
