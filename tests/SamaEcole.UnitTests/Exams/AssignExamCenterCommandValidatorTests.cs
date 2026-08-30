using FluentAssertions;
using SamaEcole.Application.Exams.Commands.AssignExamCenter;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

public class AssignExamCenterCommandValidatorTests
{
    private readonly AssignExamCenterCommandValidator _validator = new();

    private static AssignExamCenterCommand Valid() => new()
    {
        Id = Guid.NewGuid(),
        ExamCenterName = "CEM Grand Dakar",
        RowVersion = 1
    };

    [Fact]
    public void Valid_Command_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_CandidateNumber_And_CenterName_Should_Still_Pass()
    {
        // Vide = génération/héritage automatique, pas une erreur (Volume 1 §22.4).
        _validator.Validate(Valid() with { ExamCenterName = null, CandidateNumber = null }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Missing_Id_Should_Fail()
    {
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Html_In_CandidateNumber_Should_Fail()
    {
        _validator.Validate(Valid() with { CandidateNumber = "<script>1</script>" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ExamCenterCode_And_TableNumber_Are_Optional()
    {
        // Complétion progressive : la première affectation ne connaît souvent que le nom du centre.
        _validator.Validate(Valid() with { ExamCenterCode = null, TableNumber = null }).IsValid.Should().BeTrue();
        _validator.Validate(Valid() with { ExamCenterCode = "12347", TableNumber = "B-042" }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ExamCenterCode_Over_30_Chars_Should_Fail()
    {
        _validator.Validate(Valid() with { ExamCenterCode = new string('1', 31) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Html_In_TableNumber_Should_Fail()
    {
        _validator.Validate(Valid() with { TableNumber = "<b>3</b>" }).IsValid.Should().BeFalse();
    }
}
