using FluentAssertions;
using SamaEcole.Application.Registration;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Registration;

/// <summary>
/// Règles de l'offre déclarée à l'inscription (profil, cycles cochés, internat, tranche) : tout ce que le
/// Super Admin approuve et ce que le moteur de prix facture se déduit de ces fonctions pures.
/// </summary>
public class RegistrationOfferTests
{
    [Theory]
    [InlineData(ProfileType.EnseignementGeneral, true)]
    [InlineData(ProfileType.FrancoArabe, true)]
    [InlineData(ProfileType.Elementaire, true)]
    [InlineData(ProfileType.InternatDaara, true)]
    [InlineData(ProfileType.DaaraMemorisation, false)]
    [InlineData(ProfileType.ComptabiliteRapports, false)]
    public void Only_Academic_Profiles_Require_Cycles(ProfileType profile, bool required)
    {
        RegistrationOffer.RequiresCycles(profile).Should().Be(required);
    }

    [Fact]
    public void An_Elementary_Profile_Accepts_Only_Preschool_And_Primary_Cycles()
    {
        RegistrationOffer.AllowedCycles(ProfileType.Elementaire).Should().Equal(CycleType.Maternelle, CycleType.Primaire);
        RegistrationOffer.AllowedCycles(ProfileType.EnseignementGeneral).Should().Equal(
            CycleType.Maternelle, CycleType.Primaire, CycleType.College, CycleType.Lycee);
    }

    [Theory]
    [InlineData(new CycleType[0], SchoolCycleProfile.Primaire)]
    [InlineData(new[] { CycleType.Maternelle }, SchoolCycleProfile.Primaire)]
    [InlineData(new[] { CycleType.Primaire }, SchoolCycleProfile.Primaire)]
    [InlineData(new[] { CycleType.College }, SchoolCycleProfile.College)]
    [InlineData(new[] { CycleType.Lycee }, SchoolCycleProfile.Lycee)]
    [InlineData(new[] { CycleType.Primaire, CycleType.College }, SchoolCycleProfile.Bicycle)]
    [InlineData(new[] { CycleType.Maternelle, CycleType.Primaire }, SchoolCycleProfile.Bicycle)]
    [InlineData(new[] { CycleType.Maternelle, CycleType.Primaire, CycleType.College }, SchoolCycleProfile.Complexe)]
    [InlineData(new[] { CycleType.Maternelle, CycleType.Primaire, CycleType.College, CycleType.Lycee }, SchoolCycleProfile.Complexe)]
    public void The_Billing_Cycle_Profile_Follows_The_Number_Of_Cycles_Checked(CycleType[] cycles, SchoolCycleProfile expected)
    {
        RegistrationOffer.CycleProfileFor(cycles).Should().Be(expected);
    }

    [Theory]
    [InlineData(ProfileType.EnseignementGeneral, false, true, true, false, false)]
    [InlineData(ProfileType.EnseignementGeneral, true, true, true, true, false)]
    [InlineData(ProfileType.ComptabiliteRapports, false, false, true, false, false)]
    [InlineData(ProfileType.ComptabiliteRapports, true, false, true, true, false)]
    [InlineData(ProfileType.DaaraMemorisation, false, false, true, true, true)]
    [InlineData(ProfileType.FrancoArabe, false, true, true, true, true)]
    public void Modules_Follow_The_Profile_Preset_And_Boarding_Only_Ever_Adds_The_Internat_Module(
        ProfileType profile, bool hasBoarding, bool pedagogy, bool finance, bool internat, bool coran)
    {
        var modules = RegistrationOffer.ModulesFor(profile, hasBoarding);

        modules.Should().Be(new ModulePreset(pedagogy, finance, internat, coran));
    }

    [Fact]
    public void Cycles_Not_Given_Fall_Back_To_Every_Cycle_And_Given_Cycles_Are_Kept_In_Canonical_Order()
    {
        RegistrationOffer.ManagedCyclesFor(null).Should().Be("Maternelle,Primaire,College,Lycee");
        RegistrationOffer.ManagedCyclesFor([]).Should().Be("Maternelle,Primaire,College,Lycee");
        RegistrationOffer.ManagedCyclesFor([CycleType.College, CycleType.Primaire]).Should().Be("Primaire,College");
    }

    [Fact]
    public void Stored_Cycles_Round_Trip()
    {
        var stored = RegistrationOffer.SerializeCycles([CycleType.Lycee, CycleType.College]);

        stored.Should().Be("College,Lycee");
        RegistrationOffer.ParseCycles(stored).Should().Equal(CycleType.College, CycleType.Lycee);
        RegistrationOffer.SerializeCycles(null).Should().BeNull();
        RegistrationOffer.SerializeCycles([]).Should().BeNull();
        RegistrationOffer.ParseCycles(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(100, StudentQuotaTier.Tier_100, null)]
    [InlineData(101, StudentQuotaTier.Tier_100, "Tier1_150")]   // petite tranche cochée avec un effectif plus gros
    [InlineData(400, StudentQuotaTier.Tier2_400, null)]
    [InlineData(260, StudentQuotaTier.Tier_250, "Tier2_400")]
    [InlineData(80, StudentQuotaTier.Tier2_400, null)]          // surpayer reste permis
    public void The_Declared_Count_Must_Fit_The_Chosen_Tier(int declared, StudentQuotaTier chosen, string? requiredMinimum)
    {
        var problem = RegistrationOffer.Check(declared, chosen);

        if (requiredMinimum is null)
        {
            problem.Should().BeNull();
        }
        else
        {
            problem.Should().NotBeNull();
            problem!.Kind.Should().Be(OfferProblemKind.TierTooSmall);
            problem.MinimumTier.Should().Be(Enum.Parse<StudentQuotaTier>(requiredMinimum));
        }
    }

    [Theory]
    [InlineData(401)]
    [InlineData(410)]
    [InlineData(5000)]
    public void Above_The_Last_Standard_Band_The_Offer_Is_On_Quote(int declared)
    {
        var problem = RegistrationOffer.Check(declared, StudentQuotaTier.Tier2_400);

        problem.Should().NotBeNull();
        problem!.Kind.Should().Be(OfferProblemKind.RequiresQuote);
    }

    [Theory]
    [InlineData(StudentQuotaTier.Tier3_800)]
    [InlineData(StudentQuotaTier.Tier4_Custom)]
    public void A_Tier_On_Quote_Is_Never_Self_Service_Even_With_A_Small_Count(StudentQuotaTier tier)
    {
        RegistrationOffer.Check(50, tier)!.Kind.Should().Be(OfferProblemKind.RequiresQuote);
    }
}
