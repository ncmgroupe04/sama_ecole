using FluentAssertions;
using SamaEcole.Application.Finance.Commands.ApplyStandardFee;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.UpdateClassFee;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

public class FeeValidatorsTests
{
    private readonly CreateFeeCategoryCommandValidator _categoryValidator = new();
    private readonly ApplyStandardFeeCommandValidator _applyValidator = new();
    private readonly UpdateClassFeeCommandValidator _updateValidator = new();

    [Fact]
    public void Valid_Category_Should_Pass()
    {
        var command = new CreateFeeCategoryCommand { Name = "Mensualité", IsRecurring = true };

        _categoryValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Category_Name_Should_Fail()
    {
        var command = new CreateFeeCategoryCommand { Name = "", IsRecurring = false };

        _categoryValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Category_Name_With_Html_Should_Fail()
    {
        // JGK-F01 — branchement de la règle NoHtml (payloads exhaustifs : SafeTextValidationTests).
        var command = new CreateFeeCategoryCommand { Name = "<i>Frais</i>", IsRecurring = false };

        _categoryValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Frais_D_Inscription_Should_Pass()
    {
        // Le nom de catégorie LE PLUS COURANT du produit contient la sous-chaîne « script » : si la
        // règle anti-injection bloquait le mot au lieu des caractères « < » / « > », ce test tomberait.
        var command = new CreateFeeCategoryCommand { Name = "Frais d'inscription", IsRecurring = false };

        _categoryValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Apply_Standard_With_A_Zero_Amount_Should_Pass()
    {
        // Zéro est légitime : un frais peut être offert (inscription gratuite, par exemple).
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = 0 };

        _applyValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Apply_Standard_With_A_Negative_Amount_Should_Fail()
    {
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = -1 };

        _applyValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Apply_Standard_With_An_Absurd_Amount_Should_Fail()
    {
        // Garde-fou contre le zéro de trop, qui multiplierait le montant sur toutes les classes.
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.NewGuid(), Amount = 999_000_000 };

        _applyValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Apply_Standard_Without_A_Category_Should_Fail()
    {
        var command = new ApplyStandardFeeCommand { FeeCategoryId = Guid.Empty, Amount = 15000 };

        _applyValidator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Update_With_A_Valid_Amount_Should_Pass()
    {
        var command = new UpdateClassFeeCommand(Guid.NewGuid(), 15000, RowVersion: 42);

        _updateValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_With_A_Negative_Amount_Should_Fail()
    {
        var command = new UpdateClassFeeCommand(Guid.NewGuid(), -5000, RowVersion: 42);

        _updateValidator.Validate(command).IsValid.Should().BeFalse();
    }
}
