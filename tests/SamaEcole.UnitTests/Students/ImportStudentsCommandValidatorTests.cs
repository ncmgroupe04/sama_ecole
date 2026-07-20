using FluentAssertions;
using SamaEcole.Application.Students.Commands.ImportStudents;
using Xunit;

namespace SamaEcole.UnitTests.Students;

/// <summary>
/// Validation de FORME uniquement — le contenu ligne par ligne (date, classe, genre...) dépend de
/// l'état en base et n'est exercé qu'au niveau fonctionnel (StudentImportEndpointsTests), comme
/// ImportGradesCommandHandler.
/// </summary>
public class ImportStudentsCommandValidatorTests
{
    private readonly ImportStudentsCommandValidator _validator = new();

    [Fact]
    public void Should_Succeed_With_A_Valid_File()
    {
        var command = new ImportStudentsCommand([1, 2, 3], "eleves.csv", DryRun: true);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_FileContent_Is_Empty()
    {
        var command = new ImportStudentsCommand([], "eleves.csv", DryRun: true);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FileContent));
    }

    [Fact]
    public void Should_Fail_When_FileName_Is_Empty()
    {
        var command = new ImportStudentsCommand([1, 2, 3], "", DryRun: true);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FileName));
    }
}
