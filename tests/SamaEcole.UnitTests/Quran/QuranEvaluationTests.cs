using SamaEcole.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class QuranEvaluationTests
{
    [Fact]
    public void A_New_Evaluation_Starts_With_Zero_Mistakes_And_No_Score()
    {
        var evaluation = new QuranEvaluation
        {
            SchoolId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            EvaluationDate = new DateOnly(2026, 9, 20)
        };

        evaluation.MemoryMistakes.Should().Be(0);
        evaluation.TajwidMistakes.Should().Be(0);
        evaluation.Hesitations.Should().Be(0);
        evaluation.FinalScore.Should().Be(0);
    }
}
