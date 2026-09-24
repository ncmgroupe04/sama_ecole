using FluentAssertions;
using SamaEcole.Application.Classrooms.Commands.CreateClassroom;
using SamaEcole.Application.Classrooms.Commands.UpdateClassroom;
using Xunit;

namespace SamaEcole.UnitTests.Classrooms;

/// <summary>Série de la classe (Évolution N°4, arbitrages A1/A2) : liste fermée, Lycée seulement, null = sans série.</summary>
public class ClassroomSeriesValidatorTests
{
    private readonly CreateClassroomCommandValidator _create = new();
    private readonly UpdateClassroomCommandValidator _update = new();

    private static CreateClassroomCommand Create(string level, string? series) => new()
    {
        Name = "Terminale S2 A", Level = level, Capacity = 40, Series = series
    };

    private static UpdateClassroomCommand Update(string level, string? series)
        => new(Guid.NewGuid(), "Terminale S2 A", level, 40, 1u, Series: series);

    [Theory]
    [InlineData("Lycée", "S2")]
    [InlineData("Lycée", "s2")]        // normalisée par le handler : la casse ne fait pas une autre série
    [InlineData("Lycée", " TECH ")]
    [InlineData("Secondaire", "L1")]   // synonyme de « Lycée » reconnu par ClassroomCycle
    [InlineData("Lycée", null)]        // classe sans série (Seconde commune, par exemple)
    public void A_Lycee_Classroom_Accepts_A_Known_Series_Or_None(string level, string? series)
    {
        _create.Validate(Create(level, series)).IsValid.Should().BeTrue();
        _update.Validate(Update(level, series)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Lycée", "S9")]        // hors catalogue
    [InlineData("Lycée", "Scientifique")]
    public void An_Unknown_Series_Is_Refused(string level, string series)
    {
        _create.Validate(Create(level, series)).IsValid.Should().BeFalse();
        _update.Validate(Update(level, series)).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Collège")]
    [InlineData("Primaire")]
    [InlineData("Maternelle")]
    public void A_Series_Is_Refused_Outside_The_Lycee(string level)
    {
        _create.Validate(Create(level, "S2")).IsValid.Should().BeFalse();
        _update.Validate(Update(level, "S2")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void No_Series_Never_Adds_A_Constraint_To_An_Ordinary_Classroom()
    {
        _create.Validate(Create("Primaire", null)).IsValid.Should().BeTrue();
        _update.Validate(Update("Collège", null)).IsValid.Should().BeTrue();
    }
}
