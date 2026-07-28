using FluentAssertions;
using SamaEcole.Domain.Common;
using Xunit;

namespace SamaEcole.UnitTests.Common;

/// <summary>
/// L'année scolaire sénégalaise bascule en octobre, pas en janvier
/// (docs/Volume_1_Cahier_des_Charges.md §2.2).
/// </summary>
public class AcademicYearTests
{
    [Theory]
    // Année scolaire 2025-2026 : de la rentrée d'octobre 2025 jusqu'à septembre 2026.
    [InlineData("2025-10-01", 2025)] // jour de la bascule
    [InlineData("2025-12-31", 2025)]
    [InlineData("2026-01-01", 2025)] // le Nouvel An ne change PAS l'année scolaire
    [InlineData("2026-07-14", 2025)]
    [InlineData("2026-09-30", 2025)] // dernier jour avant la bascule
    // Année scolaire 2026-2027 : la rentrée d'octobre 2026 fait basculer le millésime.
    [InlineData("2026-10-01", 2026)]
    [InlineData("2027-02-15", 2026)]
    public void ForDate_Should_Roll_Over_In_October(string date, int expected)
    {
        var result = AcademicYear.ForDate(DateTimeOffset.Parse($"{date}T00:00:00Z"));

        result.Should().Be(expected);
    }
}