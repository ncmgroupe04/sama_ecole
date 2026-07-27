using FluentAssertions;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>
/// La matrice formule → fonctionnalités est lue par DEUX chemins qui doivent s'accorder :
/// FeatureAuthorizationHandler (l'API refuse) et GetSchoolFeaturesQuery (l'interface masque). Ces
/// tests figent le contrat commercial ; les faire échouer doit être un acte délibéré.
/// </summary>
public class PlanFeaturesTests
{
    [Fact]
    public void Premium_Includes_Every_Feature()
    {
        foreach (var feature in Enum.GetValues<Feature>())
        {
            PlanFeatures.Includes(SubscriptionPlan.Premium, feature)
                .Should().BeTrue($"la formule Premium doit tout inclure, or {feature} en est absente.");
        }
    }

    [Fact]
    public void Primaire_Is_The_Entry_Plan_And_Includes_No_Optional_Feature()
    {
        PlanFeatures.For(SubscriptionPlan.Primaire).Should().BeEmpty();
    }

    [Theory]
    [InlineData(SubscriptionPlan.Primaire, false)]
    [InlineData(SubscriptionPlan.Standard, false)]
    [InlineData(SubscriptionPlan.Premium, true)]
    public void Sms_Is_Premium_Only(SubscriptionPlan plan, bool expected)
    {
        PlanFeatures.Includes(plan, Feature.SmsNotifications).Should().Be(expected);
    }

    [Fact]
    public void Advanced_Financial_Reports_Start_At_Standard()
    {
        PlanFeatures.Includes(SubscriptionPlan.Primaire, Feature.AdvancedFinancialReports).Should().BeFalse();
        PlanFeatures.Includes(SubscriptionPlan.Standard, Feature.AdvancedFinancialReports).Should().BeTrue();
    }

    [Theory]
    [InlineData(Feature.AdvancedFinancialReports, SubscriptionPlan.Standard)]
    [InlineData(Feature.SmsNotifications, SubscriptionPlan.Premium)]
    [InlineData(Feature.MultiSchool, SubscriptionPlan.Premium)]
    public void MinimumPlanFor_Names_The_Cheapest_Plan_That_Includes_The_Feature(
        Feature feature, SubscriptionPlan expected)
    {
        PlanFeatures.MinimumPlanFor(feature).Should().Be(expected);
    }

    /// <summary>
    /// Le message « Passez à la formule X » (FeatureAuthorizationResultHandler) mentirait si la
    /// formule annoncée n'incluait finalement pas la fonctionnalité.
    /// </summary>
    [Fact]
    public void MinimumPlanFor_Always_Names_A_Plan_That_Really_Includes_The_Feature()
    {
        foreach (var feature in Enum.GetValues<Feature>())
        {
            var minimum = PlanFeatures.MinimumPlanFor(feature);

            PlanFeatures.Includes(minimum, feature)
                .Should().BeTrue($"la formule annoncée pour {feature} doit réellement l'inclure.");
        }
    }
}
