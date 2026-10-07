using System.Reflection;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// Garde anti-régression du lot C : <c>Enrollment.BoardingStatus</c> et <c>Enrollment.RoomId</c> sont OBSOLÈTES (le séjour vit
/// dans <c>boarding_enrollments</c>). Tant que l'attribut y est, toute nouvelle utilisation produit un avertissement de
/// compilation CS0618 (le dépôt exige 0 avertissement). Retirer l'attribut sans avoir supprimé les colonnes (lot F) ferait
/// silencieusement réapparaître un second modèle d'hébergement.
/// </summary>
public class LegacyBoardingColumnsGuardTests
{
    [Theory]
    [InlineData("BoardingStatus")]
    [InlineData("RoomId")]
    public void The_Legacy_Enrollment_Columns_Stay_Obsolete_Until_They_Are_Dropped(string property)
    {
        var member = typeof(Enrollment).GetProperty(property, BindingFlags.Public | BindingFlags.Instance);

        member.Should().NotBeNull();
        member!.GetCustomAttribute<ObsoleteAttribute>().Should().NotBeNull(
            "la colonne héritée ne doit plus être utilisée ; sa suppression est le lot F");
    }
}
