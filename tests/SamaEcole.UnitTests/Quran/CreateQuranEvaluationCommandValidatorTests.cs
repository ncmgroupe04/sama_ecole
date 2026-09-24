using SamaEcole.Application.Quran.Commands.CreateQuranEvaluation;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class CreateQuranEvaluationCommandValidatorTests
{
    private readonly CreateQuranEvaluationCommandValidator _validator = new();

    private static CreateQuranEvaluationCommand Valid() => new(
        Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow), MemoryMistakes: 0, TajwidMistakes: 0, Hesitations: 0, FinalScore: 18);

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_StudentId_Should_Fail()
    {
        _validator.Validate(Valid() with { StudentId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Future_Evaluation_Date_Should_Fail()
    {
        _validator.Validate(Valid() with { EvaluationDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Negative_Mistake_Counts_Should_Fail(int memory, int tajwid, int hesitations)
    {
        _validator.Validate(Valid() with { MemoryMistakes = memory, TajwidMistakes = tajwid, Hesitations = hesitations }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_FinalScore_Should_Fail()
    {
        _validator.Validate(Valid() with { FinalScore = -1 }).IsValid.Should().BeFalse();
    }
}
