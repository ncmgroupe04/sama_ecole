using FluentAssertions;
using SamaEcole.Application.Students.Commands.SetStudentPhoto;
using Xunit;

namespace SamaEcole.UnitTests.Students;

public class SetStudentPhotoCommandValidatorTests
{
    private readonly SetStudentPhotoCommandValidator _validator = new();

    [Fact]
    public void Should_Succeed_When_Setting_A_Valid_Photo()
    {
        var base64 = Convert.ToBase64String("photo compressée"u8.ToArray());
        var command = new SetStudentPhotoCommand(Guid.NewGuid(), base64, RowVersion: 1);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Succeed_When_Clearing_The_Photo()
    {
        // PhotoDataBase64 null = retire la photo téléversée (voir SetStudentPhotoCommand) : c'est un
        // appel valide, pas une erreur de validation.
        var command = new SetStudentPhotoCommand(Guid.NewGuid(), PhotoDataBase64: null, RowVersion: 1);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_StudentId_Is_Empty()
    {
        var command = new SetStudentPhotoCommand(Guid.Empty, PhotoDataBase64: null, RowVersion: 1);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.StudentId));
    }

    [Fact]
    public void Should_Fail_When_PhotoData_Is_Not_Valid_Base64()
    {
        var command = new SetStudentPhotoCommand(Guid.NewGuid(), "pas du base64 !!!", RowVersion: 1);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.PhotoDataBase64));
    }
}
