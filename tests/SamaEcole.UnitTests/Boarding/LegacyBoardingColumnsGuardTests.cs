using System.Reflection;
using FluentAssertions;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// Garde anti-régression du lot F1 : <c>Enrollment.BoardingStatus</c> et <c>Enrollment.RoomId</c> n'existent plus dans le code. Le séjour vit dans
/// <c>boarding_enrollments</c> ; les deux colonnes ne subsistent en base qu'en propriétés FANTÔMES de la configuration EF (schéma physique inchangé
/// jusqu'à la migration <c>DropLegacyBoardingColumns</c>, lot F2). Réintroduire la propriété CLR ferait réapparaître un second modèle d'hébergement.
/// Ce test se supprime avec les colonnes, au lot F2.
/// </summary>
public class LegacyBoardingColumnsGuardTests
{
    [Theory]
    [InlineData("BoardingStatus")]
    [InlineData("RoomId")]
    public void The_Legacy_Enrollment_Columns_Are_No_Longer_Clr_Properties(string property)
    {
        typeof(Enrollment).GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().BeNull("la colonne héritée n'est plus qu'une propriété fantôme EF ; elle ne doit plus être accessible au code métier");
    }
}
