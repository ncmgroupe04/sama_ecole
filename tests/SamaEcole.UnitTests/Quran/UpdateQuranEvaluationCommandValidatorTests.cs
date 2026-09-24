using SamaEcole.Application.Quran.Commands.UpdateQuranEvaluation;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class UpdateQuranEvaluationCommandValidatorTests
{
    private readonly UpdateQuranEvaluationCommandValidator _validator = new();

    private static UpdateQuranEvaluationCommand Valid() => new(
        Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), 0, 0, 0, 18, RowVersion: 1);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Id_Should_Fail()
    {
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_FinalScore_Should_Fail()
    {
        _validator.Validate(Valid() with { FinalScore = -1 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Future_Evaluation_Date_Should_Fail()
    {
        _validator.Validate(Valid() with { EvaluationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) }).IsValid.Should().BeFalse();
    }
}
