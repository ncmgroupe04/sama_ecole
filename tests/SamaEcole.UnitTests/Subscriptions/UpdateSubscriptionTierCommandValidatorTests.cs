using FluentAssertions;
using SamaEcole.Application.Subscriptions.Commands.UpdateSubscriptionTier;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>Cohérence de la demande du Super Admin : tranche, plafond sur mesure et statut.</summary>
public class UpdateSubscriptionTierCommandValidatorTests
{
    private readonly UpdateSubscriptionTierCommandValidator _validator = new();

    private static UpdateSubscriptionTierCommand Command(
        StudentQuotaTier? tier = null, int? custom = null, TenantSubscriptionStatus? status = null) => new()
    {
        SchoolId = Guid.NewGuid(), Tier = tier, CustomMaxStudentLimit = custom, Status = status
    };

    [Fact]
    public void A_Standard_Tier_Alone_Is_Valid() =>
        _validator.Validate(Command(StudentQuotaTier.Tier3_800)).IsValid.Should().BeTrue();

    [Fact]
    public void A_Status_Alone_Is_Valid() =>
        _validator.Validate(Command(status: TenantSubscriptionStatus.Suspended)).IsValid.Should().BeTrue();

    [Fact]
    public void A_Custom_Tier_With_A_Limit_Is_Valid() =>
        _validator.Validate(Command(StudentQuotaTier.Tier4_Custom, custom: 1200)).IsValid.Should().BeTrue();

    [Fact]
    public void An_Unlimited_Custom_Limit_Is_Valid() =>
        _validator.Validate(Command(StudentQuotaTier.Tier4_Custom, custom: int.MaxValue)).IsValid.Should().BeTrue();

    [Fact]
    public void Tier_And_Status_Can_Be_Changed_Together() =>
        _validator.Validate(Command(StudentQuotaTier.Tier1_150, status: TenantSubscriptionStatus.Active)).IsValid.Should().BeTrue();

    [Fact]
    public void A_Request_Changing_Nothing_Is_Refused() =>
        _validator.Validate(Command()).IsValid.Should().BeFalse();

    [Fact]
    public void The_Custom_Tier_Requires_A_Limit() =>
        _validator.Validate(Command(StudentQuotaTier.Tier4_Custom)).Errors
            .Should().ContainSingle(e => e.PropertyName == nameof(UpdateSubscriptionTierCommand.CustomMaxStudentLimit));

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void The_Custom_Limit_Must_Be_At_Least_One(int limit) =>
        _validator.Validate(Command(StudentQuotaTier.Tier4_Custom, custom: limit)).IsValid.Should().BeFalse();

    [Fact]
    public void A_Custom_Limit_Is_Refused_With_Any_Other_Tier() =>
        _validator.Validate(Command(StudentQuotaTier.Tier2_400, custom: 500)).IsValid.Should().BeFalse();

    [Fact]
    public void A_Custom_Limit_Without_A_Tier_Is_Refused() =>
        _validator.Validate(Command(status: TenantSubscriptionStatus.Active, custom: 500)).IsValid.Should().BeFalse();

    [Fact]
    public void The_Birth_State_Cannot_Be_Assigned() =>
        _validator.Validate(Command(status: TenantSubscriptionStatus.PendingOnboarding)).IsValid.Should().BeFalse();

    [Fact]
    public void Out_Of_Range_Enums_Are_Refused()
    {
        _validator.Validate(Command((StudentQuotaTier)99)).IsValid.Should().BeFalse();
        _validator.Validate(Command(status: (TenantSubscriptionStatus)99)).IsValid.Should().BeFalse();
    }
}
