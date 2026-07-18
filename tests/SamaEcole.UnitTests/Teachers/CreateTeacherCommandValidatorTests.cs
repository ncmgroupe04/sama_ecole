using FluentAssertions;
using SamaEcole.Application.Teachers.Commands.CreateTeacher;
using Xunit;

namespace SamaEcole.UnitTests.Teachers;

public class CreateTeacherCommandValidatorTests
{
    private readonly CreateTeacherCommandValidator _validator = new();

    private static CreateTeacherCommand Valid() => new()
    {
        FullName = "Moussa Ndiaye",
        Email = "moussa.ndiaye@sama-ecole.sn",
        SubjectIds = [Guid.NewGuid()]
    };

    [Fact]
    public void Valid_Teacher_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_FullName_Should_Fail()
    {
        _validator.Validate(Valid() with { FullName = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Invalid_Email_Should_Fail()
    {
        var result = _validator.Validate(Valid() with { Email = "pas-un-email" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTeacherCommand.Email));
    }

    [Fact]
    public void No_Subject_Should_Fail()
    {
        var result = _validator.Validate(Valid() with { SubjectIds = [] });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTeacherCommand.SubjectIds));
    }

    [Fact]
    public void Duplicate_Subject_Ids_Should_Fail()
    {
        var subjectId = Guid.NewGuid();

        var result = _validator.Validate(Valid() with { SubjectIds = [subjectId, subjectId] });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Several_Distinct_Subjects_Should_Pass()
    {
        var result = _validator.Validate(Valid() with { SubjectIds = [Guid.NewGuid(), Guid.NewGuid()] });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void FullName_With_Script_Tag_Should_Fail()
    {
        // JGK-F01 — la couverture exhaustive des payloads est dans SafeTextValidationTests ; ici on
        // prouve seulement que le champ est bien branché sur la règle.
        var result = _validator.Validate(Valid() with { FullName = "<script>alert(1)</script>" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTeacherCommand.FullName));
    }

    [Fact]
    public void Email_With_Html_Should_Fail()
    {
        // « <script>@evil.sn » contient un « @ » : EmailAddress() (mode ASP.NET Core) le laisserait
        // passer — c'est NoHtml qui doit l'arrêter.
        var result = _validator.Validate(Valid() with { Email = "<script>@evil.sn" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTeacherCommand.Email));
    }
}
