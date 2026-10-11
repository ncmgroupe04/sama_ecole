using FluentAssertions;
using SamaEcole.Application.Boarding.Leaves;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class CompanionMatcherTests
{
    private static readonly AllowedExitPerson[] Persons =
        [new() { Name = "Fatou Diop", Relationship = "Mère", Phone = "770000000" }];

    [Theory]
    [InlineData("Fatou Diop")]
    [InlineData("  fatou   DIOP ")]
    public void A_Listed_Person_Matches_Ignoring_Case_And_Extra_Spaces(string accompaniedBy) =>
        CompanionMatcher.IsListed(accompaniedBy, Persons).Should().BeTrue();

    [Theory]
    [InlineData("Moussa")]
    [InlineData("Fatou")]        // pas de correspondance partielle
    [InlineData("   ")]
    public void Anyone_Else_Is_Unlisted(string accompaniedBy) =>
        CompanionMatcher.IsListed(accompaniedBy, Persons).Should().BeFalse();

    [Fact] public void An_Empty_List_Lists_Nobody() =>
        CompanionMatcher.IsListed("Fatou Diop", []).Should().BeFalse();
}
