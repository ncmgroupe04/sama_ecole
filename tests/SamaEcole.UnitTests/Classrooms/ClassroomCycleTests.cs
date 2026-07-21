using FluentAssertions;
using SamaEcole.Application.Classrooms;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Classrooms;

/// <summary>
/// Le cycle d'une classe se DÉDUIT de son niveau. Régression d'origine : Classroom.Cycle n'était branché
/// sur aucune commande, toutes les classes restaient donc sur son défaut College — un CM2 sortait un
/// bulletin « COLLÈGE DE », noté sur /20, avec une moyenne pondérée par coefficients.
/// </summary>
public class ClassroomCycleTests
{
    /// <summary>Les 5 valeurs proposées par la liste déroulante de l'écran Classes.</summary>
    [Theory]
    [InlineData("Crèche", CycleType.Maternelle)]
    [InlineData("Maternelle", CycleType.Maternelle)]
    [InlineData("Primaire", CycleType.Primaire)]
    [InlineData("Collège", CycleType.College)]
    [InlineData("Lycée", CycleType.Lycee)]
    public void CycleFor_Maps_Every_Level_Offered_By_The_Classroom_Form(string level, CycleType expected)
    {
        ClassroomCycle.CycleFor(level).Should().Be(expected);
    }

    /// <summary>Le niveau reste un texte libre côté API : la reconnaissance ignore casse, accents et espaces.</summary>
    [Theory]
    [InlineData("primaire", CycleType.Primaire)]
    [InlineData("PRIMAIRE", CycleType.Primaire)]
    [InlineData("  Primaire  ", CycleType.Primaire)]
    [InlineData("college", CycleType.College)]
    [InlineData("COLLEGE", CycleType.College)]
    [InlineData("lycee", CycleType.Lycee)]
    [InlineData("Élémentaire", CycleType.Primaire)]
    [InlineData("Moyen", CycleType.College)]
    [InlineData("CEM", CycleType.College)]
    [InlineData("Secondaire", CycleType.Lycee)]
    public void CycleFor_Ignores_Case_Accents_And_Surrounding_Space(string level, CycleType expected)
    {
        ClassroomCycle.CycleFor(level).Should().Be(expected);
    }

    /// <summary>
    /// Niveau hors nomenclature : repli sur College (/20). Volontairement PAS sur un cycle à notation
    /// simplifiée — basculer en /10 réinterpréterait des notes déjà saisies sur /20, bien plus
    /// destructeur que l'inverse.
    /// </summary>
    [Theory]
    [InlineData("Classe préparatoire")]
    [InlineData("Formation professionnelle")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CycleFor_Falls_Back_To_College_For_An_Unknown_Level(string? level)
    {
        ClassroomCycle.CycleFor(level).Should().Be(CycleType.College);
    }

    /// <summary>
    /// Maternelle et Primaire partagent la notation simplifiée (/10, moyenne simple, sans coefficients
    /// ni appréciations) ; Collège et Lycée gardent la moyenne pondérée sur /20.
    /// </summary>
    [Theory]
    [InlineData(CycleType.Maternelle, true)]
    [InlineData(CycleType.Primaire, true)]
    [InlineData(CycleType.College, false)]
    [InlineData(CycleType.Lycee, false)]
    public void UsesSimplifiedGrading_Covers_The_Preschool_And_Primary_Cycles(CycleType cycle, bool expected)
    {
        cycle.UsesSimplifiedGrading().Should().Be(expected);
    }
}
