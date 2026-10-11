using FluentAssertions;
using SamaEcole.Application.Boarding.Dormitories.CreateDormitory;
using SamaEcole.Application.Boarding.Dormitories.UpdateDormitory;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class DormitoryCommandValidatorTests
{
    private readonly CreateDormitoryCommandValidator _validator = new();
    private readonly UpdateDormitoryCommandValidator _updateValidator = new();

    private static CreateDormitoryCommand Valid() => new()
    {
        Name = "Pavillon Oustaz Ahmad", Gender = DormitoryGender.Garcons,
        SupervisorName = "M. Ba", SupervisorPhone = "77 123 45 67"
    };

    private static UpdateDormitoryCommand ValidUpdate() => new(
        Guid.NewGuid(), "Pavillon Oustaz Ahmad", DormitoryGender.Filles, null, null, null, null, 0);

    [Fact] public void Valid_Dormitory_Passes() => _validator.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact] public void Optional_Fields_Can_Be_Absent() =>
        _validator.Validate(Valid() with { SupervisorName = null, SupervisorPhone = null, Notes = null }).IsValid.Should().BeTrue();

    [Fact] public void Empty_Name_Fails() => _validator.Validate(Valid() with { Name = "" }).IsValid.Should().BeFalse();

    [Fact] public void Name_Longer_Than_100_Fails() =>
        _validator.Validate(Valid() with { Name = new string('x', 101) }).IsValid.Should().BeFalse();

    [Fact] public void Mixte_Is_Refused_Because_It_Only_Exists_For_Data_Migration() =>
        _validator.Validate(Valid() with { Gender = DormitoryGender.Mixte }).IsValid.Should().BeFalse();

    [Fact] public void Unknown_Gender_Value_Fails() =>
        _validator.Validate(Valid() with { Gender = (DormitoryGender)99 }).IsValid.Should().BeFalse();

    [Fact] public void Invalid_Supervisor_Phone_Fails() =>
        _validator.Validate(Valid() with { SupervisorPhone = "abc" }).IsValid.Should().BeFalse();

    [Fact] public void Html_In_Name_Fails() =>
        _validator.Validate(Valid() with { Name = "<b>x</b>" }).IsValid.Should().BeFalse();

    [Fact] public void Update_Applies_The_Same_Rules() => _updateValidator.Validate(ValidUpdate()).IsValid.Should().BeTrue();

    [Fact] public void Update_Refuses_Mixte_Too() =>
        _updateValidator.Validate(ValidUpdate() with { Gender = DormitoryGender.Mixte }).IsValid.Should().BeFalse();

    [Fact] public void Update_Requires_An_Id() =>
        _updateValidator.Validate(ValidUpdate() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
}
