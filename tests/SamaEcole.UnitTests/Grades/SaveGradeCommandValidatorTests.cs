using FluentAssertions;
using SamaEcole.Application.Grades.Commands.SaveGrade;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Ticket JGK-G01 — validation de FORME de la saisie de note (le contrôle de barème — 10 ou 20 —
/// dépend de SchoolSettings et vit dans le Handler).
/// </summary>
public class SaveGradeCommandValidatorTests
{
    private readonly SaveGradeCommandValidator _validator = new();

    private static SaveGradeCommand Valid() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvaluationType.Devoir, 15m, RowVersion: null);

    [Fact]
    public void A_Well_Formed_Grade_Passes()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Student_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { StudentId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveGradeCommand.StudentId));
    }

    [Fact]
    public void An_Empty_Subject_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { SubjectId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveGradeCommand.SubjectId));
    }

    [Fact]
    public void An_Empty_Term_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { TermId = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveGradeCommand.TermId));
    }

    [Fact]
    public void An_Unknown_Evaluation_Type_Is_Rejected()
    {
        var result = _validator.Validate(Valid() with { EvaluationType = (EvaluationType)999 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveGradeCommand.EvaluationType));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void A_Negative_Value_Is_Rejected(double value)
    {
        var result = _validator.Validate(Valid() with { Value = (decimal)value });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SaveGradeCommand.Value));
    }

    [Theory]
    [InlineData(EvaluationType.Devoir)]
    [InlineData(EvaluationType.Composition)]
    public void Every_Evaluation_Type_Is_Accepted(EvaluationType type)
    {
        _validator.Validate(Valid() with { EvaluationType = type }).IsValid.Should().BeTrue();
    }
}
