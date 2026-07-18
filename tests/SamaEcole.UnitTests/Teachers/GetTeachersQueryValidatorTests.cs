using FluentAssertions;
using SamaEcole.Application.Teachers.Queries.GetTeachers;
using Xunit;

namespace SamaEcole.UnitTests.Teachers;

public class GetTeachersQueryValidatorTests
{
    private readonly GetTeachersQueryValidator _validator = new();

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(new GetTeachersQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void PageSize_Beyond_The_Cap_Should_Fail()
    {
        var query = new GetTeachersQuery { PageSize = GetTeachersQueryValidator.MaxPageSize + 1 };

        _validator.Validate(query).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_Positive_Page_Should_Fail(int page)
    {
        _validator.Validate(new GetTeachersQuery { Page = page }).IsValid.Should().BeFalse();
    }
}
