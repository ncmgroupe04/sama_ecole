using FluentAssertions;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Application.Subscriptions.Changes;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>
/// Planificateur de changement d'abonnement : à partir de l'état actuel et de ce que le Directeur tente de modifier, il
/// produit l'état CIBLE complet, dit si c'est une réduction, et nomme ce qui empêche. Pur : aucun accès base.
/// </summary>
public class SubscriptionChangePlannerTests
{
    private static readonly CycleType[] AllCycles = [CycleType.Maternelle, CycleType.Primaire, CycleType.College, CycleType.Lycee];

    private static SubscriptionState General(
        StudentQuotaTier tier = StudentQuotaTier.Tier1_150,
        IReadOnlyList<CycleType>? cycles = null,
        bool hasBoarding = false,
        ModulePreset? modules = null) => new(
        ProfileType.EnseignementGeneral, tier, cycles ?? [CycleType.Primaire, CycleType.College], hasBoarding,
        modules ?? new ModulePreset(IsPedagogyEnabled: true, IsFinanceEnabled: true, IsInternatEnabled: false, IsCoranModuleEnabled: false));

    private static ChangePlan Plan(
        SubscriptionState current, SubscriptionChangeSet change, int students = 100,
        IReadOnlyDictionary<CycleType, int>? classrooms = null) =>
        SubscriptionChangePlanner.Plan(current, change, students, classrooms ?? new Dictionary<CycleType, int>());

    // ------------------------------------------------------------------ Rien à changer

    [Fact]
    public void An_Empty_Change_Is_A_No_Op()
    {
        var plan = Plan(General(), new SubscriptionChangeSet());

        plan.IsNoOp.Should().BeTrue();
        plan.IsDowngrade.Should().BeFalse();
        plan.Lines.Should().BeEmpty();
        plan.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Asking_For_What_The_School_Already_Has_Is_A_No_Op_Whatever_The_Cycle_Order()
    {
        var current = General(cycles: [CycleType.Primaire, CycleType.College]);

        var plan = Plan(current, new SubscriptionChangeSet(
            Profile: ProfileType.EnseignementGeneral,
            Tier: StudentQuotaTier.Tier1_150,
            Cycles: [CycleType.College, CycleType.Primaire],
            IsInternatEnabled: false));

        plan.IsNoOp.Should().BeTrue();
    }

    // ------------------------------------------------------------------ Profil

    [Fact]
    public void Switching_To_A_Profile_Reapplies_Its_Module_Preset_And_Is_Not_A_Downgrade_When_Nothing_Is_Lost()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Profile: ProfileType.FrancoArabe));

        plan.IsNoOp.Should().BeFalse();
        plan.IsDowngrade.Should().BeFalse();
        plan.Target.Profile.Should().Be(ProfileType.FrancoArabe);
        plan.Target.Modules.Should().Be(new ModulePreset(true, true, true, true), "le Franco-Arabe inclut Internat et Coran");
        plan.Lines.Select(l => l.Field).Should().Contain(["Profile", "Internat", "Coran"]);
    }

    [Fact]
    public void Switching_To_A_Profile_That_Drops_A_Subscribed_Module_Is_A_Downgrade()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Profile: ProfileType.DaaraMemorisation));

        plan.IsDowngrade.Should().BeTrue("la Pédagogie (notes, bulletins) ne serait plus souscrite");
        plan.Target.Modules.IsPedagogyEnabled.Should().BeFalse();
    }

    [Fact]
    public void A_Profile_Switch_Keeps_The_Boarding_Billing_Flag_And_Adds_The_Internat_Module_For_Boarders()
    {
        var current = General(hasBoarding: true, modules: new ModulePreset(true, true, true, false));

        var plan = Plan(current, new SubscriptionChangeSet(Profile: ProfileType.ComptabiliteRapports));

        plan.Target.HasBoarding.Should().BeTrue();
        plan.Target.Modules.IsInternatEnabled.Should().BeTrue("un établissement avec internat garde le module même en Comptabilité");
    }

    [Fact]
    public void An_Elementary_Profile_Restricts_The_Cycles_To_Preschool_And_Primary()
    {
        var current = General(cycles: AllCycles);

        var plan = Plan(current, new SubscriptionChangeSet(Profile: ProfileType.Elementaire));

        plan.Target.Cycles.Should().Equal(CycleType.Maternelle, CycleType.Primaire);
        plan.IsDowngrade.Should().BeTrue("Collège et Lycée ne sont plus gérés");
    }

    [Fact]
    public void An_Elementary_Profile_Refuses_Explicit_Middle_Or_High_School_Cycles()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Profile: ProfileType.Elementaire, Cycles: [CycleType.College]));

        plan.Problems.Should().ContainSingle(p => p.Kind == PlanProblemKind.CyclesNotAllowedForProfile);
    }

    // ------------------------------------------------------------------ Tranche

    [Fact]
    public void A_Bigger_Tier_Is_An_Upgrade()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Tier: StudentQuotaTier.Tier2_400), students: 160);

        plan.IsNoOp.Should().BeFalse();
        plan.IsDowngrade.Should().BeFalse();
        plan.Target.Tier.Should().Be(StudentQuotaTier.Tier2_400);
        plan.Lines.Should().ContainSingle(l => l.Field == "Tier");
    }

    [Fact]
    public void A_Smaller_Tier_Is_A_Downgrade()
    {
        var plan = Plan(General(StudentQuotaTier.Tier2_400), new SubscriptionChangeSet(Tier: StudentQuotaTier.Tier1_150), students: 90);

        plan.IsDowngrade.Should().BeTrue();
        plan.Problems.Should().BeEmpty();
    }

    [Fact]
    public void A_Tier_Smaller_Than_The_Current_Headcount_Is_Refused()
    {
        var plan = Plan(General(StudentQuotaTier.Tier2_400), new SubscriptionChangeSet(Tier: StudentQuotaTier.Tier_100), students: 180);

        plan.Problems.Should().ContainSingle(p => p.Kind == PlanProblemKind.TierBelowHeadcount)
            .Which.Message.Should().Contain("180");
    }

    [Fact]
    public void The_Grand_Groupe_Tier_Can_Be_Requested_But_The_Custom_Tier_Cannot()
    {
        Plan(General(), new SubscriptionChangeSet(Tier: StudentQuotaTier.Tier3_800), students: 300)
            .Problems.Should().BeEmpty();

        Plan(General(), new SubscriptionChangeSet(Tier: StudentQuotaTier.Tier4_Custom), students: 300)
            .Problems.Should().ContainSingle(p => p.Kind == PlanProblemKind.TierNotRequestable);
    }

    // ------------------------------------------------------------------ Cycles

    [Fact]
    public void Adding_A_Cycle_Is_An_Upgrade()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Cycles: [CycleType.Primaire, CycleType.College, CycleType.Lycee]));

        plan.IsDowngrade.Should().BeFalse();
        plan.Target.Cycles.Should().Equal(CycleType.Primaire, CycleType.College, CycleType.Lycee);
        plan.Lines.Should().ContainSingle(l => l.Field == "Cycles");
    }

    [Fact]
    public void Removing_A_Cycle_Is_A_Downgrade()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Cycles: [CycleType.Primaire]));

        plan.IsDowngrade.Should().BeTrue();
        plan.Problems.Should().BeEmpty();
    }

    [Fact]
    public void Removing_A_Cycle_That_Still_Has_Classrooms_Is_Refused_And_Names_The_Cycle()
    {
        var classrooms = new Dictionary<CycleType, int> { [CycleType.College] = 3 };

        var plan = Plan(General(), new SubscriptionChangeSet(Cycles: [CycleType.Primaire]), classrooms: classrooms);

        plan.Problems.Should().ContainSingle(p => p.Kind == PlanProblemKind.CycleHasClassrooms)
            .Which.Message.Should().Contain("3").And.Contain("Collège");
    }

    [Fact]
    public void An_Empty_Cycle_List_Is_Refused()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Cycles: []));

        plan.Problems.Should().ContainSingle(p => p.Kind == PlanProblemKind.NoCycle);
    }

    // ------------------------------------------------------------------ Modules et internat

    [Fact]
    public void Switching_The_Internat_Module_On_Sets_The_Boarding_Billing_Flag()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(IsInternatEnabled: true));

        plan.IsNoOp.Should().BeFalse();
        plan.IsDowngrade.Should().BeFalse();
        plan.Target.HasBoarding.Should().BeTrue();
        plan.Target.Modules.IsInternatEnabled.Should().BeTrue();
        plan.Lines.Should().ContainSingle(l => l.Field == "Internat");
    }

    [Fact]
    public void Switching_The_Subscribed_Internat_Module_Off_Clears_The_Boarding_Flag_And_Is_A_Downgrade()
    {
        var current = General(hasBoarding: true, modules: new ModulePreset(true, true, true, false));

        var plan = Plan(current, new SubscriptionChangeSet(IsInternatEnabled: false));

        plan.IsDowngrade.Should().BeTrue();
        plan.Target.HasBoarding.Should().BeFalse();
    }

    [Fact]
    public void Switching_On_A_Module_The_School_Does_Not_Have_Is_An_Upgrade_Without_Touching_The_Boarding_Flag()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(IsCoranModuleEnabled: true));

        plan.IsDowngrade.Should().BeFalse();
        plan.Target.Modules.IsCoranModuleEnabled.Should().BeTrue();
        plan.Target.HasBoarding.Should().BeFalse();
    }

    // ------------------------------------------------------------------ Cumul

    [Fact]
    public void Several_Changes_At_Once_Are_Planned_Together_And_Any_Reduction_Makes_It_A_Downgrade()
    {
        var plan = Plan(
            General(),
            new SubscriptionChangeSet(Tier: StudentQuotaTier.Tier2_400, Cycles: [CycleType.Primaire]),
            students: 160);

        plan.IsDowngrade.Should().BeTrue("un cycle est retiré, même si la tranche monte");
        plan.Lines.Select(l => l.Field).Should().Contain(["Tier", "Cycles"]);
    }

    [Fact]
    public void The_Change_Lines_Read_In_French()
    {
        var plan = Plan(General(), new SubscriptionChangeSet(Profile: ProfileType.FrancoArabe, Tier: StudentQuotaTier.Tier_250), students: 120);

        plan.Lines.Single(l => l.Field == "Profile").Should().Be(new ChangeLine("Profile", "Enseignement général", "Franco-arabe"));
        plan.Lines.Single(l => l.Field == "Tier").Should().Be(new ChangeLine("Tier", "jusqu'à 150 élèves", "jusqu'à 250 élèves"));
    }
}
