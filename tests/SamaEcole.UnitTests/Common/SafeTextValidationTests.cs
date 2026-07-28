using FluentAssertions;
using SamaEcole.Application.Common.Validation;
using Xunit;

namespace SamaEcole.UnitTests.Common;

/// <summary>
/// Ticket JGK-F01 — la règle anti-injection partagée elle-même. Chaque validateur qui l'applique est
/// testé dans son propre fichier ; ici on prouve le TRACÉ de la frontière : tout ce qui permet de
/// former une balise ou un URI de script est refusé, tout le vocabulaire français légitime — y
/// compris les mots contenant « script » — passe.
/// </summary>
public class SafeTextValidationTests
{
    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<SCRIPT SRC=//evil.sn>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("Awa <b>Fall</b>")]
    [InlineData("1 < 2")]
    [InlineData("2 > 1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("javascript :alert(1)")] // espace avant le « : » — les navigateurs le tolèrent
    [InlineData("&#60;script&#62;")] // entités numériques : redeviendraient « <script> » au décodage
    public void Injection_Attempts_Should_Be_Rejected(string payload)
    {
        SafeTextValidation.IsSafeText(payload).Should().BeFalse();
    }

    [Theory]
    [InlineData("Frais d'inscription")] // contient la sous-chaîne « script » : DOIT passer
    [InlineData("Description des frais")]
    [InlineData("Awa Fall")]
    [InlineData("N'Diaye-Sarr, née à Thiès")]
    [InlineData("Établissement « Les Baobabs » & Fils")] // « & » seul est inoffensif, seul « &# » est bloqué
    [InlineData("+221 77 123 45 67")]
    [InlineData("moussa.ndiaye@sama-ecole.sn")]
    [InlineData("")]
    [InlineData(null)]
    public void Legitimate_French_Text_Should_Pass(string? text)
    {
        SafeTextValidation.IsSafeText(text).Should().BeTrue();
    }
}
