using FluentAssertions;
using SamaEcole.Application.SchoolCalendar.Commands.CreateSchoolClosure;
using SamaEcole.Application.SchoolCalendar.Commands.UpdateSchoolClosure;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.SchoolCalendar;

/// <summary>Calendrier de fermeture (Franco-Arabe Bloc 5a) : intervalle fermé et validation de saisie.</summary>
public class SchoolClosureTests
{
    private static readonly DateOnly Jour = new(2027, 6, 6);

    private static SchoolClosure Closure(DateOnly start, DateOnly end) =>
        new() { Label = "Vacances", StartDate = start, EndDate = end };

    // -- Covers : l'intervalle est FERMÉ, les deux bornes sont des jours fermés ----------------------

    [Fact]
    public void A_Single_Day_Closure_Covers_Only_That_Day()
    {
        var closure = Closure(Jour, Jour);

        closure.Covers(Jour).Should().BeTrue();
        closure.Covers(Jour.AddDays(-1)).Should().BeFalse();
        closure.Covers(Jour.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void A_Closure_Covers_Both_Of_Its_Bounds_And_Everything_Between()
    {
        var closure = Closure(new DateOnly(2026, 12, 19), new DateOnly(2027, 1, 3));

        closure.Covers(new DateOnly(2026, 12, 19)).Should().BeTrue("la borne de début est incluse");
        closure.Covers(new DateOnly(2026, 12, 31)).Should().BeTrue();
        closure.Covers(new DateOnly(2027, 1, 3)).Should().BeTrue("la borne de fin est incluse");
        closure.Covers(new DateOnly(2027, 1, 4)).Should().BeFalse();
    }

    // -- Création ------------------------------------------------------------------------------------

    private readonly CreateSchoolClosureCommandValidator _create = new();
    private readonly UpdateSchoolClosureCommandValidator _update = new();

    private static CreateSchoolClosureCommand ValidCreate() =>
        new(Guid.NewGuid(), Jour, Jour, SchoolClosureKind.Holiday, "Tabaski", "عيد الأضحى");

    [Fact]
    public void A_Complete_Closure_Passes()
        => _create.Validate(ValidCreate()).IsValid.Should().BeTrue();

    [Fact]
    public void The_Arabic_Label_Is_Optional()
        => _create.Validate(ValidCreate() with { LabelAr = null }).IsValid.Should().BeTrue();

    [Fact]
    public void An_End_Date_Before_The_Start_Date_Fails()
    {
        var result = _create.Validate(ValidCreate() with { EndDate = Jour.AddDays(-1) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateSchoolClosureCommand.EndDate));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Label_Fails(string label)
        => _create.Validate(ValidCreate() with { Label = label }).IsValid.Should().BeFalse();

    [Fact]
    public void A_Too_Long_Label_Fails()
        => _create.Validate(ValidCreate() with { Label = new string('A', 121) }).IsValid.Should().BeFalse();

    [Fact]
    public void A_Too_Long_Arabic_Label_Fails()
        => _create.Validate(ValidCreate() with { LabelAr = new string('ع', 121) }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Html_Bearing_Label_Fails()
        => _create.Validate(ValidCreate() with { Label = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Unknown_Kind_Fails()
        => _create.Validate(ValidCreate() with { Kind = (SchoolClosureKind)99 }).IsValid.Should().BeFalse();

    [Fact]
    public void A_Missing_Year_Fails()
        => _create.Validate(ValidCreate() with { SchoolYearId = Guid.Empty }).IsValid.Should().BeFalse();

    // -- Modification --------------------------------------------------------------------------------

    private static UpdateSchoolClosureCommand ValidUpdate() => new()
    {
        Id = Guid.NewGuid(), StartDate = Jour, EndDate = Jour.AddDays(2),
        Kind = SchoolClosureKind.Vacation, Label = "Vacances", LabelAr = null
    };

    [Fact]
    public void A_Valid_Update_Passes()
        => _update.Validate(ValidUpdate()).IsValid.Should().BeTrue();

    [Fact]
    public void An_Update_With_An_Inverted_Period_Fails()
        => _update.Validate(ValidUpdate() with { EndDate = Jour.AddDays(-3) }).IsValid.Should().BeFalse();

    [Fact]
    public void An_Update_With_A_Blank_Label_Fails()
        => _update.Validate(ValidUpdate() with { Label = " " }).IsValid.Should().BeFalse();
}
