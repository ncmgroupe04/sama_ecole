using FluentAssertions;
using SamaEcole.Application.Coefficients;
using Xunit;

namespace SamaEcole.UnitTests.Coefficients;

public class SubjectCoefficientsTests
{
    [Theory]
    [InlineData(4, null, null, 4)]      // rien : la matière, comportement historique
    [InlineData(4, null, 6, 6)]         // série
    [InlineData(4, 8, null, 8)]         // classe
    [InlineData(4, 8, 6, 8)]            // la classe l'emporte sur la série
    public void Precedence_Is_Classroom_Then_Series_Then_Subject(
        int baseValue, int? classroom, int? series, int expected)
        => SubjectCoefficients.Resolve(baseValue, classroom, series).Should().Be(expected);

    [Fact]
    public void An_Override_Of_Another_Subject_Never_Applies()
    {
        var mathsId = Guid.NewGuid();
        var overrides = new CoefficientOverrides(
            new Dictionary<Guid, decimal> { [mathsId] = 9m }, new Dictionary<Guid, decimal>());

        overrides.Effective(Guid.NewGuid(), 3m).Should().Be(3m);
        overrides.Effective(mathsId, 3m).Should().Be(9m);
    }

    [Fact]
    public void None_Changes_Nothing()
        => CoefficientOverrides.None.Effective(Guid.NewGuid(), 2.5m).Should().Be(2.5m);
}
