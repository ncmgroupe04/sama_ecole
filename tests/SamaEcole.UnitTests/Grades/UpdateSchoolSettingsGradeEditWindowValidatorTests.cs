using SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;
using SamaEcole.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// GradeEditWindowDays : fenêtre de correction des notes par l'Enseignant (Évolution N°1). Bornée à
/// 1–365 jours comme DebtorReminderThresholdDays : 0 verrouillerait toute correction dès la saisie,
/// et une valeur démesurée viderait la règle de son sens.
/// </summary>
public class UpdateSchoolSettingsGradeEditWindowValidatorTests
{
    private readonly UpdateSchoolSettingsCommandValidator _validator = new();

    private static UpdateSchoolSettingsCommand Valid() => new(
        GradingScale: "20",
        StudentMatriculeFormat: "ELEV-{YEAR}-{SEQ:4}",
        TeacherMatriculeFormat: "ENS-{YEAR}-{SEQ:3}",
        AutoLogoutMinutes: 10,
        DateFormat: "dd/MM/yyyy",
        TuitionMonthsPerYear: 9,
        AllowSecretaryToManageGrading: false,
        AllowFinanceToModifyFees: false,
        AllowFinanceToDeleteFees: false);

    [Fact]
    public void Default_Window_Should_Pass_And_Be_Seven_Days()
    {
        SchoolSettingsDefaults.GradeEditWindowDays.Should().Be(7);
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public void Window_Inside_Bounds_Should_Pass(int days)
    {
        _validator.Validate(Valid() with { GradeEditWindowDays = days }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(366)]
    public void Window_Outside_Bounds_Should_Fail(int days)
    {
        var result = _validator.Validate(Valid() with { GradeEditWindowDays = days });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateSchoolSettingsCommand.GradeEditWindowDays));
    }
}
