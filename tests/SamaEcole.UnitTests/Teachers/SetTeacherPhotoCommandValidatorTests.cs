using FluentAssertions;
using SamaEcole.Application.Teachers.Commands.SetTeacherPhoto;
using Xunit;

namespace SamaEcole.UnitTests.Teachers;

public class SetTeacherPhotoCommandValidatorTests
{
    private readonly SetTeacherPhotoCommandValidator _validator = new();

    [Fact]
    public void Should_Succeed_When_Setting_A_Valid_Photo()
    {
        var base64 = Convert.ToBase64String("photo compressée"u8.ToArray());
        var command = new SetTeacherPhotoCommand(Guid.NewGuid(), base64, RowVersion: 1);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Succeed_When_Clearing_The_Photo()
    {
        var command = new SetTeacherPhotoCommand(Guid.NewGuid(), PhotoDataBase64: null, RowVersion: 1);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_TeacherId_Is_Empty()
    {
        var command = new SetTeacherPhotoCommand(Guid.Empty, PhotoDataBase64: null, RowVersion: 1);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.TeacherId));
    }

    [Fact]
    public void Should_Fail_When_PhotoData_Is_Not_Valid_Base64()
    {
        var command = new SetTeacherPhotoCommand(Guid.NewGuid(), "pas du base64 !!!", RowVersion: 1);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.PhotoDataBase64));
    }
}
