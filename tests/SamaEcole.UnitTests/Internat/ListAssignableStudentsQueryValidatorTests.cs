using FluentAssertions;
using SamaEcole.Application.Internat.Queries.ListAssignableStudents;
using Xunit;

namespace SamaEcole.UnitTests.Internat;

public class ListAssignableStudentsQueryValidatorTests
{
    private readonly ListAssignableStudentsQueryValidator _validator = new();

    [Fact]
    public void The_Default_Query_Is_Valid_And_Targets_Students_Without_An_Oustaz()
    {
        var query = new ListAssignableStudentsQuery();

        query.Scope.Should().Be("unassigned");
        _validator.Validate(query).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("all")]
    [InlineData("ALL")]
    public void The_Two_Known_Scopes_Are_Valid(string scope) =>
        _validator.Validate(new ListAssignableStudentsQuery { Scope = scope }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("everyone")]
    [InlineData("assigned")]
    public void An_Unknown_Scope_Fails(string scope) =>
        _validator.Validate(new ListAssignableStudentsQuery { Scope = scope }).IsValid.Should().BeFalse();

    [Fact]
    public void The_Search_Is_Bounded_And_Free_Of_Markup()
    {
        _validator.Validate(new ListAssignableStudentsQuery { Search = new string('a', 100) }).IsValid.Should().BeTrue();
        _validator.Validate(new ListAssignableStudentsQuery { Search = new string('a', 101) }).IsValid.Should().BeFalse();
        _validator.Validate(new ListAssignableStudentsQuery { Search = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void An_Arabic_Search_Is_Accepted() =>
        _validator.Validate(new ListAssignableStudentsQuery { Search = "عائشة" }).IsValid.Should().BeTrue();
}
