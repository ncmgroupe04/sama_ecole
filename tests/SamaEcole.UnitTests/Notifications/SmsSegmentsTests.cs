using FluentAssertions;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Notifications;

/// <summary>
/// Le compte de segments décide de ce qui est DÉBITÉ du solde d'une école (SmsDispatcher) et de ce
/// que rapporte l'adaptateur après envoi (HttpSmsService). Une erreur ici se traduit directement en
/// crédits perdus ou en SMS offerts.
/// </summary>
public class SmsSegmentsTests
{
    [Fact]
    public void Empty_Body_Still_Costs_One_Segment()
    {
        SmsSegments.Count(string.Empty).Should().Be(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(160)]
    public void Short_Gsm7_Message_Fits_In_One_Segment(int length)
    {
        SmsSegments.Count(new string('a', length)).Should().Be(1);
    }

    [Fact]
    public void Gsm7_Message_Just_Over_The_Limit_Costs_Two_Segments()
    {
        SmsSegments.Count(new string('a', 161)).Should().Be(2);
    }

    /// <summary>
    /// Les accents français courants appartiennent à l'alphabet GSM-7 : un rappel de scolarité
    /// rédigé correctement ne doit pas coûter le double d'un texte sans accents.
    /// </summary>
    [Fact]
    public void Common_French_Accents_Stay_On_The_Gsm7_Budget()
    {
        var body = new string('é', 150);

        SmsSegments.Count(body).Should().Be(1);
    }

    /// <summary>
    /// Un caractère hors GSM-7 (émoji, caractère non latin) bascule TOUT le message en UCS-2, dont la
    /// limite est bien plus basse — c'est le piège classique de la facturation SMS.
    /// </summary>
    [Fact]
    public void A_Single_Non_Gsm7_Character_Downgrades_The_Whole_Message_To_Unicode()
    {
        var body = new string('a', 100) + "😀";

        SmsSegments.Count(body).Should().BeGreaterThan(1);
    }

    [Fact]
    public void Unicode_Message_Fits_Seventy_Characters_In_One_Segment()
    {
        SmsSegments.Count(new string('世', 70)).Should().Be(1);
        SmsSegments.Count(new string('世', 71)).Should().Be(2);
    }
}
