using FluentAssertions;
using SamaEcole.Application.Exams.Commands.UpdateExamDossier;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

public class UpdateExamDossierCommandValidatorTests
{
    private readonly UpdateExamDossierCommandValidator _validator = new();

    private static UpdateExamDossierCommand Valid() => new()
    {
        Id = Guid.NewGuid(),
        BirthCertificatePresent = true,
        CivilStatusConforming = true,
        RowVersion = 1
    };

    [Fact]
    public void Valid_Update_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Missing_Id_Should_Fail()
    {
        _validator.Validate(Valid() with { Id = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Null_CivilStatusConforming_Should_Pass()
    {
        // Nul = non encore contrôlé, un état parfaitement légitime (Volume 1 §22.2).
        _validator.Validate(Valid() with { CivilStatusConforming = null }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Html_In_CivilStatusNotes_Should_Fail()
    {
        _validator.Validate(Valid() with { CivilStatusNotes = "<img src=x onerror=alert(1)>" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Null_CivilRegistryDocumentStatus_Should_Pass()
    {
        // null = champ omis : la valeur en base est préservée, ce n'est pas une erreur.
        _validator.Validate(Valid() with { CivilRegistryDocumentStatus = null }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void EnRegularisation_Should_Pass()
    {
        // L'état qui donne sa raison d'être au champ : « fourni, non conforme, jugement supplétif en cours ».
        _validator.Validate(Valid() with { CivilRegistryDocumentStatus = CivilRegistryDocumentStatus.EnRegularisation })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Out_Of_Range_CivilRegistryDocumentStatus_Should_Fail()
    {
        _validator.Validate(Valid() with { CivilRegistryDocumentStatus = (CivilRegistryDocumentStatus)99 })
            .IsValid.Should().BeFalse();
    }
}
