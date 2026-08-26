using FluentAssertions;
using SamaEcole.Application.Exams.Queries.GetExamDossiers;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

public class GetExamDossiersQueryValidatorTests
{
    private readonly GetExamDossiersQueryValidator _validator = new();

    [Fact]
    public void Default_Query_Should_Pass()
    {
        _validator.Validate(new GetExamDossiersQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Zero_Page_Should_Fail()
    {
        _validator.Validate(new GetExamDossiersQuery { Page = 0 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Above_100_Should_Fail()
    {
        _validator.Validate(new GetExamDossiersQuery { PageSize = 101 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Zero_Should_Fail()
    {
        _validator.Validate(new GetExamDossiersQuery { PageSize = 0 }).IsValid.Should().BeFalse();
    }
}
