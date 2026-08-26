using FluentAssertions;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

public class CreateExamDossierCommandValidatorTests
{
    private readonly CreateExamDossierCommandValidator _validator = new();

    private static CreateExamDossierCommand Valid() => new()
    {
        ExamSessionId = Guid.NewGuid(),
        StudentId = Guid.NewGuid(),
        ClassroomId = Guid.NewGuid()
    };

    [Fact]
    public void Valid_Dossier_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Missing_ExamSessionId_Should_Fail()
    {
        _validator.Validate(Valid() with { ExamSessionId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Missing_StudentId_Should_Fail()
    {
        _validator.Validate(Valid() with { StudentId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Missing_ClassroomId_Should_Fail()
    {
        _validator.Validate(Valid() with { ClassroomId = Guid.Empty }).IsValid.Should().BeFalse();
    }
}
