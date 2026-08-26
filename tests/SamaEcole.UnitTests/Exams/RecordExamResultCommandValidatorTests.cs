using FluentAssertions;
using SamaEcole.Application.Exams.Commands.RecordExamResult;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

public class RecordExamResultCommandValidatorTests
{
    private readonly RecordExamResultCommandValidator _validator = new();

    private static RecordExamResultCommand Valid() => new()
    {
        ExamDossierId = Guid.NewGuid(),
        IsAdmitted = true,
        Mention = ExamMention.Bien,
        DeliberatedOn = new DateOnly(2027, 7, 10)
    };

    [Fact]
    public void Valid_Result_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Not_Admitted_Without_Mention_Should_Pass()
    {
        _validator.Validate(Valid() with { IsAdmitted = false, Mention = null }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Mention_On_A_Non_Admitted_Candidate_Should_Fail()
    {
        // Une mention ne se conçoit que pour un admis.
        _validator.Validate(Valid() with { IsAdmitted = false, Mention = ExamMention.Bien }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_AverageScore_Should_Fail()
    {
        _validator.Validate(Valid() with { AverageScore = -1 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Missing_DeliberatedOn_Should_Fail()
    {
        _validator.Validate(Valid() with { DeliberatedOn = default }).IsValid.Should().BeFalse();
    }
}
