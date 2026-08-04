using FluentAssertions;
using SamaEcole.Application.Classrooms;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Classrooms;

/// <summary>
/// Reconnaissance du NIVEAU d'une classe depuis son nom — la donnée dont dépend la délibération à double
/// niveau (<see cref="ClassroomPromotion"/>) et la liste déroulante « second niveau validé » du formulaire.
///
/// Aucune colonne ne porte le niveau : il vit dans le nom saisi librement par l'école, et chacune écrit
/// le sien à sa façon (« 6e A », « 6 ème A », « Sixième A »). Une reconnaissance trop stricte ferait
/// retomber ces classes sur leur nom brut et l'historique académique deviendrait incomparable d'une
/// école à l'autre.
/// </summary>
public class ClassroomGradeLevelsTests
{
    [Theory]
    [InlineData("CI", CycleType.Primaire, "CI")]
    [InlineData("CM2 B", CycleType.Primaire, "CM2")]
    [InlineData("ce1 a", CycleType.Primaire, "CE1")]
    [InlineData("GS 2", CycleType.Maternelle, "GS")]
    [InlineData("Grande Section", CycleType.Maternelle, "GS")]
    [InlineData("6e A", CycleType.College, "Sixième")]
    [InlineData("6 ème A", CycleType.College, "Sixième")]
    [InlineData("Troisième B", CycleType.College, "Troisième")]
    [InlineData("2nde S", CycleType.Lycee, "Seconde")]
    [InlineData("Tle S1", CycleType.Lycee, "Terminale")]
    public void The_Level_Is_Read_From_The_Class_Name_Whatever_The_Spelling(
        string classroomName, CycleType cycle, string expected)
    {
        ClassroomGradeLevels.FromClassroomName(classroomName, cycle).Should().Be(expected);
    }

    [Theory]
    [InlineData("CI-CP", CycleType.Primaire, "CI")]
    [InlineData("CE1/CE2", CycleType.Primaire, "CE1")]
    [InlineData("6e-5e", CycleType.College, "Sixième")]
    public void A_Bridge_Class_Name_Yields_Its_STARTING_Level(
        string classroomName, CycleType cycle, string expected)
    {
        // Le trait d'union DOIT être une frontière reconnue : sans lui, « CI-CP » — le nom même d'une
        // classe passerelle — ne serait rattaché à aucun niveau, et la délibération à double niveau
        // n'aurait plus de premier niveau à valider.
        ClassroomGradeLevels.FromClassroomName(classroomName, cycle).Should().Be(expected);
    }

    [Fact]
    public void The_Very_Small_Section_Is_Not_Mistaken_For_The_Small_One()
    {
        // « TPS » contient « PS » : c'est l'ordre pédagogique de la table qui départage.
        ClassroomGradeLevels.FromClassroomName("TPS", CycleType.Maternelle).Should().Be("TPS");
        ClassroomGradeLevels.FromClassroomName("PS A", CycleType.Maternelle).Should().Be("PS");
    }

    [Theory]
    [InlineData("Groupe Coranique 1", CycleType.Primaire)]
    [InlineData("", CycleType.Primaire)]
    [InlineData(null, CycleType.College)]
    public void A_Name_Outside_Any_Nomenclature_Returns_Null(string? classroomName, CycleType cycle)
    {
        // Null, jamais un niveau approché : l'appelant décide alors d'afficher le nom tel qu'il a été
        // saisi, plutôt que de se voir attribuer un niveau qu'aucune école n'a déclaré.
        ClassroomGradeLevels.FromClassroomName(classroomName, cycle).Should().BeNull();
    }

    [Fact]
    public void The_Dropdown_Offers_The_Levels_Of_The_Cycle_In_Teaching_Order()
    {
        ClassroomGradeLevels.For(CycleType.Primaire).Should().Equal("CI", "CP", "CE1", "CE2", "CM1", "CM2");
        ClassroomGradeLevels.For("Lycée").Should().Equal("Seconde", "Première", "Terminale");
    }

    [Theory]
    [InlineData("CP", CycleType.Primaire, true)]
    [InlineData("cp", CycleType.Primaire, true)]
    [InlineData("Premiere", CycleType.Lycee, true)]
    [InlineData("CP", CycleType.College, false)]
    [InlineData("Terminale", CycleType.Primaire, false)]
    [InlineData(null, CycleType.Primaire, false)]
    public void A_Target_Level_Is_Only_Accepted_Within_Its_Own_Cycle(string? level, CycleType cycle, bool expected)
    {
        // Garde-fou de la validation serveur : la liste déroulante propose déjà les bons niveaux, mais
        // l'API reste ouverte et un appel direct ne doit pas y glisser un niveau d'un autre cycle.
        ClassroomGradeLevels.IsKnown(level, cycle).Should().Be(expected);
    }
}
