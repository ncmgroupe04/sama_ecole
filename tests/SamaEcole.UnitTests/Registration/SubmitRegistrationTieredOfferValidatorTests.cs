using FluentAssertions;
using SamaEcole.Application.Registration.Commands.SubmitRegistrationRequest;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Registration;

/// <summary>
/// Soumission d'une OFFRE PAR TRANCHE D'EFFECTIF : profil, internat, cycles cochés, tranche et effectif exact.
/// La règle anti-fraude (tranche ≥ tranche minimale de l'effectif, « Grand Groupe » au-delà de 400) est portée par
/// RegistrationOffer ; ces tests prouvent qu'elle atteint bien la réponse 422 de la soumission, sur le bon champ.
/// </summary>
public class SubmitRegistrationTieredOfferValidatorTests
{
    private readonly SubmitRegistrationRequestValidator _validator = new();

    private static SubmitRegistrationRequestCommand TieredCommand() => new()
    {
        DirectorFullName = "Awa Ndiaye",
        DirectorEmail = "awa@filaos.sn",
        DirectorPhone = "+221771234567",
        DirectorPassword = "Correct-Horse-9",
        SchoolName = "Complexe Les Baobabs",
        Ownership = SchoolOwnership.Private,
        Profile = ProfileType.EnseignementGeneral,
        Tier = StudentQuotaTier.Tier1_150,
        EstimatedStudentCount = 120,
        Cycles = [CycleType.Primaire, CycleType.College]
    };

    [Fact]
    public void A_Complete_Tiered_Offer_Needs_Neither_Cycle_Profile_Nor_Size()
    {
        // CycleProfile et SizeTier (grille historique) sont absents : l'offre par tranche les remplace.
        _validator.Validate(TieredCommand()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Daara_Of_Pure_Memorisation_Needs_No_Cycles()
    {
        var command = TieredCommand() with { Profile = ProfileType.DaaraMemorisation, Cycles = null, Tier = StudentQuotaTier.Tier_100, EstimatedStudentCount = 60 };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Accounting_Profile_Needs_No_Cycles()
    {
        var command = TieredCommand() with { Profile = ProfileType.ComptabiliteRapports, Cycles = [] };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Boarding_School_Is_Accepted()
    {
        _validator.Validate(TieredCommand() with { HasBoarding = true }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(ProfileType.EnseignementGeneral)]
    [InlineData(ProfileType.FrancoArabe)]
    public void An_Academic_Profile_Must_Check_At_Least_One_Cycle(ProfileType profile)
    {
        var command = TieredCommand() with { Profile = profile, Cycles = [] };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.Cycles));
    }

    [Theory]
    [InlineData(ProfileType.EnseignementGeneral, CycleType.Maternelle)]   // préscolaire seul
    [InlineData(ProfileType.EnseignementGeneral, CycleType.Primaire)]     // élémentaire seul
    [InlineData(ProfileType.FrancoArabe, CycleType.Maternelle)]
    [InlineData(ProfileType.FrancoArabe, CycleType.Primaire)]
    [InlineData(ProfileType.Elementaire, CycleType.Maternelle)]
    [InlineData(ProfileType.Elementaire, CycleType.Primaire)]
    public void A_School_Can_Check_A_Single_Cycle_Preschool_Only_Or_Elementary_Only(ProfileType profile, CycleType cycle)
    {
        var command = TieredCommand() with { Profile = profile, Cycles = [cycle] };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Elementary_Profile_Cannot_Check_Middle_Or_High_School()
    {
        var command = TieredCommand() with { Profile = ProfileType.Elementaire, Cycles = [CycleType.Primaire, CycleType.College] };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.Cycles));
    }

    [Fact]
    public void Duplicate_Cycles_Are_Refused()
    {
        var command = TieredCommand() with { Cycles = [CycleType.Primaire, CycleType.Primaire] };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.Cycles));
    }

    [Fact]
    public void The_Exact_Student_Count_Is_Required()
    {
        var command = TieredCommand() with { EstimatedStudentCount = null };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.EstimatedStudentCount));
    }

    [Fact]
    public void A_Tier_Needs_A_Profile_And_A_Profile_Needs_A_Tier()
    {
        _validator.Validate(TieredCommand() with { Profile = null }).Errors
            .Should().Contain(e => e.PropertyName == nameof(SubmitRegistrationRequestCommand.Profile));
        _validator.Validate(TieredCommand() with { Tier = null }).Errors
            .Should().Contain(e => e.PropertyName == nameof(SubmitRegistrationRequestCommand.Tier));
    }

    [Fact]
    public void A_Public_School_Cannot_Take_A_Tiered_Offer()
    {
        var command = TieredCommand() with { Ownership = SchoolOwnership.Public };

        _validator.Validate(command).Errors.Should().Contain(e => e.PropertyName == nameof(command.Ownership));
    }

    [Fact]
    public void Ticking_A_Small_Tier_With_A_Bigger_Count_Is_Refused_And_Names_The_Minimum_Tier()
    {
        var command = TieredCommand() with { Tier = StudentQuotaTier.Tier_100, EstimatedStudentCount = 130 };

        var errors = _validator.Validate(command).Errors;

        errors.Should().ContainSingle(e => e.PropertyName == nameof(command.Tier))
            .Which.ErrorMessage.Should().Contain("130").And.Contain("150");
    }

    [Theory]
    [InlineData(StudentQuotaTier.Tier2_400, 410)]
    [InlineData(StudentQuotaTier.Tier2_400, 5000)]
    [InlineData(StudentQuotaTier.Tier3_800, 120)]
    [InlineData(StudentQuotaTier.Tier4_Custom, 120)]
    public void Above_400_Students_Or_On_A_Quote_Tier_The_Offer_Points_To_The_Grand_Groupe(StudentQuotaTier tier, int count)
    {
        var command = TieredCommand() with { Tier = tier, EstimatedStudentCount = count };

        _validator.Validate(command).Errors.Should()
            .ContainSingle(e => e.PropertyName == nameof(command.Tier))
            .Which.ErrorMessage.Should().Contain("Grand Groupe");
    }

    [Fact]
    public void Paying_For_A_Bigger_Tier_Than_Needed_Is_Allowed()
    {
        var command = TieredCommand() with { Tier = StudentQuotaTier.Tier2_400, EstimatedStudentCount = 80 };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_Historical_Offer_Is_Unchanged_When_No_Profile_Or_Tier_Is_Given()
    {
        var command = new SubmitRegistrationRequestCommand
        {
            DirectorFullName = "Awa Ndiaye", DirectorEmail = "awa@filaos.sn", DirectorPhone = "+221771234567",
            DirectorPassword = "Correct-Horse-9", SchoolName = "Complexe Les Baobabs",
            Ownership = SchoolOwnership.Private, CycleProfile = null, SizeTier = null
        };

        var errors = _validator.Validate(command).Errors;

        errors.Should().Contain(e => e.PropertyName == nameof(command.CycleProfile));
        errors.Should().Contain(e => e.PropertyName == nameof(command.SizeTier));
    }
}
