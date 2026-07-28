using FluentAssertions;
using SamaEcole.Application.Teachers.Commands.ImportTeachers;
using Xunit;

namespace SamaEcole.UnitTests.Teachers;

/// <summary>
/// Validation de FORME uniquement — le contenu ligne par ligne (e-mail, téléphone, matières...) dépend
/// de l'état en base et n'est exercé qu'au niveau fonctionnel (TeacherImportEndpointsTests), comme
/// ImportStudentsCommandValidatorTests.
/// </summary>
public class ImportTeachersCommandValidatorTests
{
    private readonly ImportTeachersCommandValidator _validator = new();

    [Fact]
    public void Should_Succeed_With_A_Valid_File()
    {
        var command = new ImportTeachersCommand([1, 2, 3], "enseignants.csv", DryRun: true);

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_Fail_When_FileContent_Is_Empty()
    {
        var command = new ImportTeachersCommand([], "enseignants.csv", DryRun: true);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FileContent));
    }

    [Fact]
    public void Should_Fail_When_FileName_Is_Empty()
    {
        var command = new ImportTeachersCommand([1, 2, 3], "", DryRun: true);

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(command.FileName));
    }
}
