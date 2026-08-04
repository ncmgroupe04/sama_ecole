using FluentAssertions;
using SamaEcole.Application.Common;
using Xunit;

namespace SamaEcole.UnitTests.Common;

/// <summary>
/// Le formatage d'un numéro est un confort d'AFFICHAGE. Ces tests fixent surtout sa limite : tout ce
/// qui sort du gabarit sénégalais doit ressortir intact, jamais regroupé de force en 2‑3‑2‑2 — un faux
/// numéro d'apparence crédible serait bien plus nuisible qu'un affichage brut.
/// </summary>
public class PhoneFormatterTests
{
    [Theory]
    [InlineData("770000000", "77 000 00 00")]
    [InlineData("781234567", "78 123 45 67")]
    [InlineData("338250000", "33 825 00 00")] // fixe Dakar
    public void FormatSenegal_Groups_A_National_Number_As_Two_Three_Two_Two(string input, string expected)
    {
        PhoneFormatter.FormatSenegal(input).Should().Be(expected);
    }

    /// <summary>Saisie déjà espacée, ou ponctuée : le regroupement doit être idempotent.</summary>
    [Theory]
    [InlineData("77 000 00 00")]
    [InlineData("77.000.00.00")]
    [InlineData("77-000-00-00")]
    [InlineData("  770000000  ")]
    public void FormatSenegal_Normalises_Whatever_Separators_Were_Typed(string input)
    {
        PhoneFormatter.FormatSenegal(input).Should().Be("77 000 00 00");
    }

    /// <summary>Les trois écritures de l'indicatif convergent vers une seule forme affichée.</summary>
    [Theory]
    [InlineData("+221770000000")]
    [InlineData("00221770000000")]
    [InlineData("221770000000")]
    [InlineData("+221 77 000 00 00")]
    public void FormatSenegal_Keeps_The_Country_Code_In_A_Single_Canonical_Form(string input)
    {
        PhoneFormatter.FormatSenegal(input).Should().Be("+221 77 000 00 00");
    }

    /// <summary>
    /// Hors gabarit : renvoyé TEL QUEL. C'est le comportement le plus important de ce formateur —
    /// il ne doit jamais inventer un découpage sur un numéro qu'il ne reconnaît pas.
    /// </summary>
    [Theory]
    [InlineData("12345")]                 // trop court
    [InlineData("7700000000")]            // 10 chiffres, pas 9
    [InlineData("+33612345678")]          // numéro français
    [InlineData("77 000 00 00 poste 12")] // mention libre
    [InlineData("ext. 4021")]
    [InlineData("+221 77 000 00 00 / +221 78 111 22 33")] // deux numéros dans un même champ
    public void FormatSenegal_Leaves_Anything_Outside_The_Senegalese_Pattern_Untouched(string input)
    {
        PhoneFormatter.FormatSenegal(input).Should().Be(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatSenegal_Passes_Through_An_Absent_Value(string? input)
    {
        PhoneFormatter.FormatSenegal(input).Should().Be(input);
    }

    /// <summary>Le repli est le rôle de l'appelant : FormatSenegalOr le factorise pour les tableaux.</summary>
    [Fact]
    public void FormatSenegalOr_Falls_Back_When_No_Number_Is_Recorded()
    {
        PhoneFormatter.FormatSenegalOr(null).Should().Be("—");
        PhoneFormatter.FormatSenegalOr("   ").Should().Be("—");
        PhoneFormatter.FormatSenegalOr(null, "Non renseigné").Should().Be("Non renseigné");
        PhoneFormatter.FormatSenegalOr("770000000").Should().Be("77 000 00 00");
    }
}
