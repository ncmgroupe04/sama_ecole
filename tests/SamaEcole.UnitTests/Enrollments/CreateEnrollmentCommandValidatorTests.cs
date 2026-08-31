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

    // ---------------------------------------------------------------- Modèle hybride (volet 1)

    [Fact]
    public void A_Cashier_Send_With_No_Collected_Fees_Should_Pass()
    {
        // « Inscrire l'élève — Envoyer en Caisse » : on engage la dette sans encaisser. Liste de
        // frais vide, aucune session de caisse — la forme est valide, le Handler fera le reste.
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            IsDirectPayment = false
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Cashier_Send_That_Also_Collects_Fees_Should_Fail()
    {
        // Engager la dette ET encaisser sont deux gestes distincts : envoyer des CollectedFees avec
        // IsDirectPayment = false est une confusion d'intention, refusée (422) plutôt qu'ignorée.
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            IsDirectPayment = false,
            CollectedFees = [new CollectedFeeInput(Guid.NewGuid())]
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.CollectedFees));
    }

    [Fact]
    public void A_Direct_Payment_That_Collects_Fees_Should_Still_Pass()
    {
        // Contre-épreuve : la règle ne mord QUE sur IsDirectPayment = false. Le chemin historique
        // (encaissement du jour) accepte évidemment toujours des CollectedFees.
        var command = new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = Guid.NewGuid(),
            FullName = "Awa Ndiaye",
            BirthDate = new DateOnly(2015, 3, 12),
            BirthPlace = "Dakar",
            Gender = "F",
            IsDirectPayment = true,
            CollectedFees = [new CollectedFeeInput(Guid.NewGuid())]
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }
}
