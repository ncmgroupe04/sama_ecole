using FluentAssertions;
using SamaEcole.Infrastructure.Documents;
using Xunit;

namespace SamaEcole.UnitTests.ReportCards;

/// <summary>Évolution N°2 — le titre du bulletin suit le libellé de la période (trimestre, semestre, période).</summary>
public class BulletinTitleTests
{
    [Theory]
    [InlineData("1er semestre", "BULLETIN DU 1ER SEMESTRE")]
    [InlineData("2e semestre", "BULLETIN DU 2E SEMESTRE")]
    [InlineData("1er trimestre", "BULLETIN DU 1ER TRIMESTRE")]
    [InlineData("3e trimestre", "BULLETIN DU 3E TRIMESTRE")]
    [InlineData("1re période", "BULLETIN DE LA 1RE PÉRIODE")]
    [InlineData("4e période", "BULLETIN DE LA 4E PÉRIODE")]
    [InlineData("  2e semestre  ", "BULLETIN DU 2E SEMESTRE")]
    public void Title_Follows_The_Period_Label(string label, string expected)
        => BulletinTitle.For(label).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_Label_Falls_Back_To_The_Historical_Title(string? label)
        => BulletinTitle.For(label).Should().Be("BULLETIN DE NOTES");
}
