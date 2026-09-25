using FluentAssertions;
using SamaEcole.Application.Subjects.Commands.CreateSubject;
using SamaEcole.Application.Subjects.Commands.UpdateSubject;
using Xunit;

namespace SamaEcole.UnitTests.Subjects;

public class OptionalSubjectValidatorTests
{
    private static CreateSubjectCommand Create(bool isOptional = false, string? group = null, Guid? parent = null) => new()
    {
        Name = "Arabe", Level = "Collège", Coefficient = 2,
        IsOptional = isOptional, OptionGroup = group, ParentSubjectId = parent
    };

    private static UpdateSubjectCommand Update(bool isOptional = false, string? group = null, Guid? parent = null) =>
        new(Guid.NewGuid(), "Arabe", "Collège", 2, 1u, parent, null, 0, null, null, null, isOptional, group);

    [Fact]
    public void A_Mandatory_Subject_Without_Group_Is_Valid_As_Before()
        => new CreateSubjectCommandValidator().Validate(Create()).IsValid.Should().BeTrue();

    [Fact]
    public void An_Optional_Subject_With_A_Group_Is_Valid()
        => new CreateSubjectCommandValidator().Validate(Create(isOptional: true, group: "LV2")).IsValid.Should().BeTrue();

    [Fact]
    public void A_Group_Without_The_Optional_Flag_Is_Refused()
    {
        var result = new CreateSubjectCommandValidator().Validate(Create(isOptional: false, group: "LV2"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OptionGroup");
    }

    [Fact]
    public void An_Optional_Subject_Cannot_Be_An_Activity_Of_A_Domain()
    {
        var result = new CreateSubjectCommandValidator().Validate(Create(isOptional: true, parent: Guid.NewGuid()));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "IsOptional");
    }

    [Fact]
    public void A_Group_Longer_Than_Fifty_Characters_Is_Refused()
    {
        var result = new CreateSubjectCommandValidator().Validate(Create(isOptional: true, group: new string('x', 51)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OptionGroup");
    }

    [Fact]
    public void The_Same_Rules_Apply_On_Update()
    {
        var validator = new UpdateSubjectCommandValidator();

        validator.Validate(Update(isOptional: true, group: "LV2")).IsValid.Should().BeTrue();
        validator.Validate(Update(isOptional: false, group: "LV2")).Errors.Should().ContainSingle(e => e.PropertyName == "OptionGroup");
        validator.Validate(Update(isOptional: true, parent: Guid.NewGuid())).Errors.Should().ContainSingle(e => e.PropertyName == "IsOptional");
    }
}
