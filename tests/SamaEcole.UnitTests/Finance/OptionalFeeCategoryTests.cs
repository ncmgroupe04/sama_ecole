using FluentAssertions;
using SamaEcole.Application.Finance.Commands.CreateFeeCategory;
using SamaEcole.Application.Finance.Commands.UpdateFeeCategory;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.Finance;

/// <summary>
/// Frais optionnels, Tâche 1 — le drapeau <see cref="FeeCategory.IsOptional"/> : un frais que la famille
/// choisit à l'inscription (uniforme, tenue de sport) plutôt que d'un frais dû par tous.
///
/// Le défaut OBLIGATOIRE est la protection des frais essentiels (inscription, mensualité) : la liste des
/// catégories est libre et aucun nom n'est codé en dur, donc seule une catégorie explicitement marquée
/// optionnelle par le Directeur peut un jour être décochée à l'inscription. Une catégorie existante, ou
/// créée par un ancien client qui ignore le drapeau, reste due par tous.
/// </summary>
public class OptionalFeeCategoryTests
{
    private readonly CreateFeeCategoryCommandValidator _createValidator = new();
    private readonly UpdateFeeCategoryCommandValidator _updateValidator = new();

    [Fact]
    public void A_Fee_Category_Is_Mandatory_By_Default()
    {
        var category = new FeeCategory { Name = "Inscription" };

        category.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void Create_Command_Is_Mandatory_By_Default()
    {
        // Un ancien client qui n'envoie pas le champ ne doit jamais créer un frais décochable.
        var command = new CreateFeeCategoryCommand { Name = "Mensualité", IsRecurring = true };

        command.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void An_Optional_One_Off_Category_Should_Pass()
    {
        var command = new CreateFeeCategoryCommand { Name = "Tenue de sport", IsOptional = true };

        _createValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Recurring_Category_Should_Pass()
    {
        // Une cantine ou un transport mensuels sont à la fois récurrents et facultatifs.
        var command = new CreateFeeCategoryCommand { Name = "Cantine", IsRecurring = true, IsOptional = true };

        _createValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Boarding_Category_Should_Fail()
    {
        // La pension a déjà son propre choix explicite (IncludeBoardingFee + régime d'hébergement). Lui
        // ajouter une seconde case créerait deux mécanismes concurrents pour la même décision.
        var command = new CreateFeeCategoryCommand { Name = "Pension", IsRecurring = true, IsBoardingFee = true, IsOptional = true };

        var result = _createValidator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateFeeCategoryCommand.IsOptional));
    }

    [Fact]
    public void Update_With_A_Category_Id_Should_Pass()
    {
        var command = new UpdateFeeCategoryCommand(Guid.NewGuid(), IsOptional: true);

        _updateValidator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_With_An_Empty_Category_Id_Should_Fail()
    {
        var command = new UpdateFeeCategoryCommand(Guid.Empty, IsOptional: true);

        _updateValidator.Validate(command).IsValid.Should().BeFalse();
    }
}
