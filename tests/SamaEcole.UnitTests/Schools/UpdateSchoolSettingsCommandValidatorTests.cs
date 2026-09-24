using FluentAssertions;
using SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

/// <summary>Découpage de l'année (Évolution N°2) : type connu, nombre de périodes borné seulement pour « Custom ».</summary>
public class UpdateSchoolSettingsCommandValidatorTests
{
    private readonly UpdateSchoolSettingsCommandValidator _validator = new();

    private static UpdateSchoolSettingsCommand ValidCommand() => new(
        GradingScale: SchoolSettingsDefaults.GradingScale.ToString(),
        StudentMatriculeFormat: SchoolSettingsDefaults.StudentMatriculeFormat,
        TeacherMatriculeFormat: SchoolSettingsDefaults.TeacherMatriculeFormat,
        AutoLogoutMinutes: SchoolSettingsDefaults.AutoLogoutMinutes,
        DateFormat: SchoolSettingsDefaults.DateFormat,
        TuitionMonthsPerYear: SchoolSettingsDefaults.TuitionMonthsPerYear,
        AllowSecretaryToManageGrading: false,
        AllowFinanceToModifyFees: false,
        AllowFinanceToDeleteFees: false);

    [Fact]
    public void Default_Command_Is_Valid()
        => _validator.Validate(ValidCommand()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("Trimester")]
    [InlineData("Semester")]
    [InlineData("Custom")]
    [InlineData("semester")]
    public void Known_Period_Types_Are_Accepted(string type)
        => _validator.Validate(ValidCommand() with { EvaluationPeriodType = type, CustomPeriodCount = 4 })
            .IsValid.Should().BeTrue();

    [Theory]
    [InlineData("Quarterly")]
    [InlineData("")]
    [InlineData("7")]
    public void Unknown_Period_Type_Is_Rejected(string type)
        => _validator.Validate(ValidCommand() with { EvaluationPeriodType = type })
            .IsValid.Should().BeFalse();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void Custom_Count_Outside_2_To_6_Is_Rejected_Only_For_Custom(int count)
    {
        _validator.Validate(ValidCommand() with { EvaluationPeriodType = "Custom", CustomPeriodCount = count })
            .IsValid.Should().BeFalse();

        // Hors Personnalisé le nombre est ignoré : un ancien navigateur qui renvoie 3 ne doit pas être refusé.
        _validator.Validate(ValidCommand() with { EvaluationPeriodType = "Semester", CustomPeriodCount = count })
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    public void Custom_Count_Bounds_Are_Inclusive(int count)
        => _validator.Validate(ValidCommand() with { EvaluationPeriodType = "Custom", CustomPeriodCount = count })
            .IsValid.Should().BeTrue();

    // Semaine de travail (Évolution N°3).

    [Fact]
    public void Working_Days_Left_Out_Are_Valid_And_Mean_Unchanged()
        => _validator.Validate(ValidCommand() with { WorkingDays = null }).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("Saturday", "Sunday", "Monday", "Tuesday", "Wednesday")]   // repos jeudi/vendredi
    [InlineData("Monday", "Tuesday", "Wednesday", "Thursday", "Friday")]
    [InlineData("monday")]                                                  // un seul jour, casse ignorée
    [InlineData("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday")] // semaine pleine
    public void Valid_Working_Weeks_Are_Accepted(params string[] days)
        => _validator.Validate(ValidCommand() with { WorkingDays = days }).IsValid.Should().BeTrue();

    [Fact]
    public void An_Empty_Working_Week_Is_Rejected() // aucun jour : l'école ne pourrait plus rien saisir
        => _validator.Validate(ValidCommand() with { WorkingDays = [] }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("Monday", "Monday")]   // doublon
    [InlineData("Monday", "Funday")]   // inconnu
    [InlineData("1", "2")]             // numérique
    public void Invalid_Working_Weeks_Are_Rejected(params string[] days)
        => _validator.Validate(ValidCommand() with { WorkingDays = days }).IsValid.Should().BeFalse();
}
