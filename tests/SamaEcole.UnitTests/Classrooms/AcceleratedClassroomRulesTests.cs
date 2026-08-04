using FluentAssertions;
using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using SamaEcole.Application.Classrooms.Commands.UpdateClassroom;
using Xunit;

namespace SamaEcole.UnitTests.Classrooms;

/// <summary>
/// Saisie d'une classe PASSERELLE / ACCÉLÉRÉE. La liste déroulante de l'écran ne propose déjà que des
/// niveaux valides, mais l'API reste ouverte (AGENTS.md règle #9) : ces règles sont ce qui empêche un
/// appel direct d'enregistrer une passerelle incohérente — et, symétriquement, ce qui garantit qu'une
/// classe ORDINAIRE n'hérite d'aucune contrainte nouvelle.
/// </summary>
public class AcceleratedClassroomRulesTests
{
    private static CreateClassroomCommand Create(bool isAccelerated, string? targetLevel, string name = "CI-CP") => new()
    {
        Name = name,
        Level = "Primaire",
        Capacity = 30,
        IsAccelerated = isAccelerated,
        TargetLevel = targetLevel
    };

    private static IEnumerable<string> ErrorsOn(CreateClassroomCommand command) =>
        new CreateClassroomCommandValidator().Validate(command).Errors
            .Where(e => e.PropertyName == nameof(CreateClassroomCommand.TargetLevel))
            .Select(e => e.ErrorMessage);

    [Fact]
    public void An_Ordinary_Class_Needs_No_Target_Level()
    {
        // Le cas de l'immense majorité des classes : l'option ne durcit rien pour qui ne s'en sert pas.
        new CreateClassroomCommandValidator().Validate(Create(isAccelerated: false, targetLevel: null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Accelerated_Class_Must_Declare_Its_Second_Level()
    {
        ErrorsOn(Create(isAccelerated: true, targetLevel: null))
            .Should().ContainSingle().Which.Should().Contain("second niveau");
    }

    [Fact]
    public void A_Second_Level_From_Another_Cycle_Is_Refused()
    {
        // « Terminale » n'appartient pas au Primaire : la passerelle serait ininterprétable en délibération.
        ErrorsOn(Create(isAccelerated: true, targetLevel: "Terminale"))
            .Should().ContainSingle().Which.Should().Contain("cycle");
    }

    [Fact]
    public void A_Second_Level_Identical_To_The_Starting_One_Is_Refused()
    {
        // Une passerelle valide DEUX niveaux différents ; « CI → CI » n'accélère rien.
        ErrorsOn(Create(isAccelerated: true, targetLevel: "CI", name: "CI A"))
            .Should().ContainSingle().Which.Should().Contain("différent");
    }

    [Fact]
    public void A_Coherent_Bridge_Class_Is_Accepted()
    {
        new CreateClassroomCommandValidator().Validate(Create(isAccelerated: true, targetLevel: "CP"))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_Same_Rules_Apply_When_Correcting_An_Existing_Class()
    {
        // Création et correction partagent la MÊME définition (AcceleratedClassroomRules) : ce test est
        // là pour qu'un durcissement d'un côté ne puisse pas oublier l'autre.
        var command = new UpdateClassroomCommand(
            Guid.NewGuid(), "CI-CP", "Primaire", 30, RowVersion: 1,
            IsAccelerated: true, TargetLevel: "Terminale");

        new UpdateClassroomCommandValidator().Validate(command).Errors
            .Should().ContainSingle(e => e.PropertyName == nameof(UpdateClassroomCommand.TargetLevel));
    }

    [Fact]
    public void A_Class_Named_Outside_Any_Nomenclature_Is_Not_Blocked()
    {
        // « Groupe Coranique 1 » ne donne aucun niveau de départ à comparer : la règle laisse passer
        // plutôt que de bloquer une école dont la nomenclature nous échappe.
        new CreateClassroomCommandValidator()
            .Validate(Create(isAccelerated: true, targetLevel: "CP", name: "Groupe Coranique 1"))
            .IsValid.Should().BeTrue();
    }
}
