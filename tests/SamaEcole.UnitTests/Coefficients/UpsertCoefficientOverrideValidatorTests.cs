using FluentAssertions;
using SamaEcole.Application.Coefficients.Commands;
using SamaEcole.Application.Coefficients.Queries;
using SamaEcole.Application.Common.Interfaces;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

/// <summary>Surcharge de coefficient (Évolution N°4) : bornes, portée exclusive, série du catalogue, audit.</summary>
public class UpsertCoefficientOverrideValidatorTests
{
    private readonly UpsertCoefficientOverrideCommandValidator _validator = new();

    private static UpsertCoefficientOverrideCommand BySeries(decimal coefficient = 6m, string? series = "S2") => new()
    {
        SubjectId = Guid.NewGuid(), Coefficient = coefficient, Series = series
    };

    private static UpsertCoefficientOverrideCommand ByClassroom(decimal coefficient = 6m) => new()
    {
        SubjectId = Guid.NewGuid(), Coefficient = coefficient, ClassroomId = Guid.NewGuid()
    };

    [Fact]
    public void A_Series_Override_And_A_Classroom_Override_Are_Valid()
    {
        _validator.Validate(BySeries()).IsValid.Should().BeTrue();
        _validator.Validate(ByClassroom()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(20.01)]
    [InlineData(21)]
    public void A_Coefficient_Outside_Zero_To_Twenty_Is_Refused(double coefficient)
        => _validator.Validate(BySeries((decimal)coefficient)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0.01)]
    [InlineData(1.5)]
    [InlineData(20)]
    public void The_Bounds_Of_Subject_Coefficient_Are_Inclusive_At_Both_Ends(double coefficient)
        => _validator.Validate(BySeries((decimal)coefficient)).IsValid.Should().BeTrue();

    [Fact]
    public void Exactly_One_Scope_Is_Required()
    {
        var both = new UpsertCoefficientOverrideCommand
        {
            SubjectId = Guid.NewGuid(), Coefficient = 6m, Series = "S2", ClassroomId = Guid.NewGuid()
        };
        var none = new UpsertCoefficientOverrideCommand { SubjectId = Guid.NewGuid(), Coefficient = 6m };

        _validator.Validate(both).IsValid.Should().BeFalse();
        _validator.Validate(none).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("s2", true)]      // normalisée par le handler
    [InlineData(" TECH ", true)]
    [InlineData("S9", false)]
    [InlineData("Scientifique", false)]
    public void The_Series_Must_Belong_To_The_Closed_Catalogue(string series, bool valid)
        => _validator.Validate(BySeries(series: series)).IsValid.Should().Be(valid);

    [Fact]
    public void A_Blank_Series_Counts_As_No_Series()
    {
        // « » n'est pas une portée : avec une classe à côté, c'est une surcharge de classe valide.
        var command = new UpsertCoefficientOverrideCommand
        {
            SubjectId = Guid.NewGuid(), Coefficient = 6m, Series = "  ", ClassroomId = Guid.NewGuid()
        };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Every_Write_Command_Is_Audited()
    {
        typeof(IAuditableRequest).IsAssignableFrom(typeof(UpsertCoefficientOverrideCommand)).Should().BeTrue();
        typeof(IAuditableRequest).IsAssignableFrom(typeof(DeleteCoefficientOverrideCommand)).Should().BeTrue();
        typeof(IAuditableRequest).IsAssignableFrom(typeof(CarryOverCoefficientsCommand)).Should().BeTrue();
    }

    [Fact]
    public void The_Grid_Query_Needs_Exactly_One_Known_Scope()
    {
        var validator = new GetCoefficientGridQueryValidator();

        validator.Validate(new GetCoefficientGridQuery("S2", null)).IsValid.Should().BeTrue();
        validator.Validate(new GetCoefficientGridQuery(null, Guid.NewGuid())).IsValid.Should().BeTrue();
        validator.Validate(new GetCoefficientGridQuery(null, null)).IsValid.Should().BeFalse();
        validator.Validate(new GetCoefficientGridQuery("S2", Guid.NewGuid())).IsValid.Should().BeFalse();
        validator.Validate(new GetCoefficientGridQuery("S9", null)).IsValid.Should().BeFalse();
    }
}
