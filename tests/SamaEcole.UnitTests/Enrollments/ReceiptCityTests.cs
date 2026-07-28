using FluentAssertions;
using SamaEcole.Application.Enrollments;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Ticket JGK-E02 — la ligne « Fait à [ville] » du reçu. La ville est dérivée de l'adresse libre de
/// l'école (dernier segment, convention sénégalaise) ; sans adresse exploitable, on ne montre RIEN
/// plutôt qu'une valeur inventée, et le reçu laisse la mention à compléter à la main.
/// </summary>
public class ReceiptCityTests
{
    [Theory]
    [InlineData("Rue 12, Médina, Dakar", "Dakar")]
    [InlineData("Avenue Bourguiba, Dakar", "Dakar")]
    [InlineData("Touba", "Touba")]
    [InlineData("  Quartier Escale ,  Ziguinchor  ", "Ziguinchor")]
    public void FromAddress_Returns_The_Last_Segment_As_The_City(string address, string expected)
    {
        ReceiptCity.FromAddress(address).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Rue 12,")]
    public void FromAddress_Returns_Null_When_No_Usable_City(string? address)
    {
        ReceiptCity.FromAddress(address).Should().BeNull();
    }
}
