using FluentAssertions;
using SamaEcole.Application.Grades.Commands.ImportGrades;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Validation de FORME uniquement (l'existence de la classe/matière/trimestre, la résolution de
/// chaque ligne du fichier et le barème dépendent de l'état en base : ils vivent dans le Handler,
/// comme CreateGradeCommandValidator).
/// </summary>
public class ImportGradesCommandValidatorTests
{
    private readonly ImportGradesCommandValidator _validator = new();

    private static ImportGradesCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvaluationType.Devoir, [1, 2, 3], "notes.csv");

    [Fact]
    public void A_Well_Formed_Command_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Classroom_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { ClassroomId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ImportGradesCommand.ClassroomId));
    }

    [Fact]
    public void An_Empty_Subject_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { SubjectId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ImportGradesCommand.SubjectId));
    }

    [Fact]
    public void An_Empty_Term_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { TermId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ImportGradesCommand.TermId));
    }

    [Fact]
    public void An_Unknown_Evaluation_Type_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { EvaluationType = (EvaluationType)999 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ImportGradesCommand.EvaluationType));
    }

    [Fact]
    public void An_Empty_File_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { FileContent = [] });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ImportGradesCommand.FileContent));
    }

    [Fact]
    public void A_Missing_File_Name_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { FileName = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ImportGradesCommand.FileName));
    }
}
