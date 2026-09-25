using FluentAssertions;
using SamaEcole.Application.Absences.Commands.CreateLateArrival;
using Xunit;

namespace SamaEcole.UnitTests.Absences;

/// <summary>Billet d'entrée (Évolution N°5) : sans cours visé le validateur est celui d'avant ; avec, les minutes suivent la ligne d'appel.</summary>
public class CreateLateArrivalCommandValidatorTests
{
    private readonly CreateLateArrivalCommandValidator _validator = new();

    private static CreateLateArrivalCommand Command(int minutes, Guid? slot = null) => new()
    {
        StudentId = Guid.NewGuid(), Date = new DateTime(2026, 9, 26), Minutes = minutes, Reason = "Transport", TargetScheduleSlotId = slot
    };

    [Theory]
    [InlineData(1)]
    [InlineData(45)]
    [InlineData(240)]
    public void A_Ticket_With_A_Target_Accepts_1_To_240_Minutes(int minutes)
        => _validator.Validate(Command(minutes, Guid.NewGuid())).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    [InlineData(600)]
    public void A_Ticket_With_A_Target_Refuses_Minutes_Outside_The_Attendance_Bounds(int minutes)
        => _validator.Validate(Command(minutes, Guid.NewGuid())).IsValid.Should().BeFalse();

    [Fact]
    public void Without_A_Target_The_Historical_Rule_Is_Unchanged_Only_A_Positive_Number_Is_Required()
    {
        _validator.Validate(Command(600)).IsValid.Should().BeTrue("aucune borne haute avant l'évolution : rien ne change sans cours visé");
        _validator.Validate(Command(0)).IsValid.Should().BeFalse();
    }
}
