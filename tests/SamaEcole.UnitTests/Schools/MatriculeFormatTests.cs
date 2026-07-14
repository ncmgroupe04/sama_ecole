using FluentAssertions;
using SamaEcole.Domain.Common;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>
/// Ticket JGK-B02 — le gabarit de matricule devient paramétrable par établissement.
/// C'est ce qui donne enfin un sens au réglage studentMatriculeFormat : jusqu'ici, le format était
/// codé en dur dans MatriculeGenerator.
/// </summary>
public class MatriculeFormatTests
{
    [Theory]
    [InlineData("ELEV-{YEAR}-{SEQ:4}", 2026, 1, "ELEV-2026-0001")]
    [InlineData("ENS-{YEAR}-{SEQ:3}", 2026, 7, "ENS-2026-007")]
    [InlineData("{YEAR}/{SEQ:5}", 2025, 42, "2025/00042")]
    [InlineData("BAOBAB-{SEQ:2}", 2026, 3, "BAOBAB-03")]
    [InlineData("{SEQ}", 2026, 9, "9")] // sans largeur : aucune complétion
    public void Render_Should_Substitute_Year_And_Sequence(string format, int year, int seq, string expected)
    {
        MatriculeFormat.Render(format, year, seq).Should().Be(expected);
    }

    [Fact]
    public void A_Sequence_Larger_Than_Its_Width_Should_Not_Be_Truncated()
    {
        // Le 10 000e élève d'un {SEQ:4} : il vaut mieux un matricule plus long qu'un doublon.
        MatriculeFormat.Render("ELEV-{YEAR}-{SEQ:4}", 2026, 12345).Should().Be("ELEV-2026-12345");
    }

    [Fact]
    public void A_Format_Without_A_Sequence_Must_Be_Refused()
    {
        // LE piège du ticket : sans {SEQ}, tous les élèves reçoivent le MÊME matricule. La violation
        // d'unicité n'apparaîtrait qu'à la deuxième inscription — en pleine journée de rentrée.
        var error = MatriculeFormat.Validate("ELEV-{YEAR}");

        error.Should().NotBeNull();
        error.Should().Contain("SEQ");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_Empty_Format_Must_Be_Refused(string? format)
    {
        MatriculeFormat.Validate(format).Should().NotBeNull();
    }

    [Fact]
    public void An_Unknown_Token_Must_Be_Refused()
    {
        // {MONTH} n'existe pas : il serait recopié tel quel dans le matricule de chaque élève.
        var error = MatriculeFormat.Validate("ELEV-{MONTH}-{SEQ:4}");

        error.Should().NotBeNull();
        error.Should().Contain("MONTH");
    }

    [Fact]
    public void A_Format_Producing_An_Overlong_Matricule_Must_Be_Refused()
    {
        var error = MatriculeFormat.Validate(new string('X', 60) + "{SEQ:4}");

        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("ELEV-{YEAR}-{SEQ:4}")]
    [InlineData("ENS-{YEAR}-{SEQ:3}")]
    [InlineData("{SEQ:6}")]
    public void Valid_Formats_Should_Be_Accepted(string format)
    {
        MatriculeFormat.Validate(format).Should().BeNull();
    }
}
