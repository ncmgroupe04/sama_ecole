using FluentAssertions;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Ticket JGK-E01 — les règles de validation dépendent du type de mouvement : une nouvelle
/// inscription exige l'état civil de l'élève, une réinscription exige un élève existant.
/// </summary>
public class CreateEnrollmentCommandValidatorTests
{
    private readonly CreateEnrollmentCommandValidator _validator = new();

    [Fact]
    public void A_Valid_New_Enrollment_Should_Pass()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F"
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_New_Enrollment_Without_A_BirthPlace_Should_Fail()
    {
        // Feature E — lieu de naissance obligatoire pour une nouvelle inscription (l'élève créé ici
        // suit la même règle que CreateStudentCommand).
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.BirthPlace));
    }

    [Fact]
    public void A_Valid_Re_Enrollment_Should_Pass()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.ReEnrollment,
            ClassroomId = Guid.NewGuid(),
            StudentId = Guid.NewGuid()
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_New_Enrollment_Without_A_Name_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FullName));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("")]
    public void A_New_Enrollment_With_An_Invalid_Gender_Should_Fail(string gender)
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = gender
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.Gender));
    }

    [Fact]
    public void A_New_Enrollment_With_A_Future_Birth_Date_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.BirthDate));
    }

    [Fact]
    public void A_Re_Enrollment_Without_A_Student_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.ReEnrollment,
            ClassroomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.StudentId));
    }

    [Fact]
    public void An_Externe_Enrollment_With_A_RoomId_Should_Fail()
    {
        // Module Internat — symétrique de la règle "RoomId requis si non-Externe" : un RoomId sur un
        // régime Externe contournerait la garde IsInternatEnabled et fausserait le compte d'occupation
        // d'une chambre (voir CreateEnrollmentCommandValidator).
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            BoardingStatus = BoardingStatus.Externe,
            RoomId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.RoomId));
    }

    [Fact]
    public void An_Enrollment_Without_A_Classroom_Should_Fail()
    {
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.Empty,
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            Gender = "F"
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.ClassroomId));
    }
}
