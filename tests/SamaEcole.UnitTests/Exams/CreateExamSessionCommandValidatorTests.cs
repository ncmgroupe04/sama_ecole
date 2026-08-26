using FluentAssertions;
using SamaEcole.Application.Exams.Commands.CreateExamSession;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

public class CreateExamSessionCommandValidatorTests
{
    private readonly CreateExamSessionCommandValidator _validator = new();

    private static CreateExamSessionCommand Valid() => new()
    {
        SchoolYearId = Guid.NewGuid(),
        ExamType = ExamType.BFEM,
        Series = "G"
    };

    [Fact]
    public void Valid_Session_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Cfee_Without_Series_Should_Pass()
    {
        // Le CFEE n'a pas de série (Volume 1 §22.1) : Series null n'est pas une erreur de saisie.
        _validator.Validate(Valid() with { ExamType = ExamType.CFEE, Series = null }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Missing_SchoolYearId_Should_Fail()
    {
        _validator.Validate(Valid() with { SchoolYearId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Html_In_Series_Should_Fail()
    {
        _validator.Validate(Valid() with { Series = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }
}
