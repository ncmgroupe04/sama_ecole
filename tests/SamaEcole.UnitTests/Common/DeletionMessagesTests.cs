using FluentAssertions;
using SamaEcole.Application.Common.Deletion;
using Xunit;

namespace SamaEcole.UnitTests.Common;

public class DeletionMessagesTests
{
    [Fact]
    public void The_Student_Financial_Message_Is_The_Mandatory_Text()
    {
        DeletionMessages.StudentFinancialBlock.Should().Be(
            "Impossible de supprimer cet élève car il possède des transactions financières / reçus enregistrés.");
    }

    [Theory]
    [InlineData(1, "1 inscription")]
    [InlineData(3, "3 inscriptions")]
    public void A_Count_Label_Takes_The_Right_Plural(int count, string expected)
    {
        DeletionMessages.CountLabel(count, "inscription", "inscriptions").Should().Be(expected);
    }

    [Theory]
    [InlineData(new[] { "1 inscription" }, "1 inscription")]
    [InlineData(new[] { "1 inscription", "3 matières suivies" }, "1 inscription et 3 matières suivies")]
    [InlineData(new[] { "1 inscription", "3 matières suivies", "12 notes" }, "1 inscription, 3 matières suivies et 12 notes")]
    public void Labels_Are_Joined_In_French(string[] labels, string expected)
    {
        DeletionMessages.JoinLabels(labels).Should().Be(expected);
    }

    [Fact]
    public void The_Dependencies_Message_Lists_Every_Link_Precisely()
    {
        var links = new[]
        {
            new DeletionLink("Enrollment", "1 inscription", 1, true, "Annuler l'inscription et l'archiver"),
            new DeletionLink("Grade", "12 notes", 12, true, "Archiver les notes")
        };

        DeletionMessages.DependenciesBlock("Cet élève est actuellement rattaché", links).Should().Be(
            "Cet élève est actuellement rattaché à 1 inscription et 12 notes. "
            + "Vous devez rompre ces liens avant de pouvoir effectuer la suppression.");
    }
}
