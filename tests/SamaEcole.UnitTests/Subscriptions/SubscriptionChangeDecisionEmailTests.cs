using FluentAssertions;
using SamaEcole.Application.Subscriptions.Changes;
using Xunit;

namespace SamaEcole.UnitTests.Subscriptions;

/// <summary>E-mail de décision au Directeur : ce qu'il dit, ce qu'il ne dit jamais, et ses cas limites (sans nom, sans motif).</summary>
public class SubscriptionChangeDecisionEmailTests
{
    private static readonly IReadOnlyList<ChangeLine> Changes =
    [
        new("Tier", "jusqu'à 150 élèves", "jusqu'à 250 élèves"),
        new("Cycles", "Primaire", "Primaire, Collège")
    ];

    private const string Login = "https://unikol.sn/login";

    [Fact]
    public void An_Approval_Lists_What_Changed_And_Greets_The_Director_By_Name()
    {
        var mail = SubscriptionChangeDecisionEmail.Build("Fatou Sarr", "fatou@ecole.sn", Changes, approved: true, "Règlement reçu.", Login);

        mail.To.Should().Be("fatou@ecole.sn");
        mail.Subject.Should().Be("Votre demande de changement d'abonnement est validée");
        mail.Body.Should().StartWith("Bonjour Fatou Sarr,");
        mail.Body.Should().Contain(" - jusqu'à 150 élèves → jusqu'à 250 élèves").And.Contain(" - Primaire → Primaire, Collège");
        mail.Body.Should().Contain("Message de notre équipe : Règlement reçu.").And.Contain(Login);
    }

    [Fact]
    public void A_Rejection_Gives_The_Reason_And_Says_The_Subscription_Is_Unchanged()
    {
        var mail = SubscriptionChangeDecisionEmail.Build("Fatou Sarr", "fatou@ecole.sn", Changes, approved: false, "Règlement non reçu.", Login);

        mail.Subject.Should().Be("Votre demande de changement d'abonnement n'a pas été retenue");
        mail.Body.Should().Contain("Motif : Règlement non reçu.").And.Contain("reste inchangé");
        mail.Body.Should().NotContain("jusqu'à 250 élèves", "un rejet ne détaille pas un changement qui n'a pas eu lieu");
    }

    [Fact]
    public void Without_A_Name_Or_A_Note_The_Mail_Is_Still_Well_Formed()
    {
        var approval = SubscriptionChangeDecisionEmail.Build(null, "x@y.sn", Changes, approved: true, "   ", Login);
        var rejection = SubscriptionChangeDecisionEmail.Build("  ", "x@y.sn", Changes, approved: false, null, Login);

        approval.Body.Should().StartWith("Bonjour,").And.NotContain("Message de notre équipe");
        rejection.Body.Should().StartWith("Bonjour,").And.NotContain("Motif");
        rejection.Body.Should().Contain("reste inchangé");
    }

    [Fact]
    public void No_Change_Line_Leaves_No_Empty_List()
    {
        var mail = SubscriptionChangeDecisionEmail.Build("Awa", "x@y.sn", [], approved: true, null, Login);

        mail.Body.Should().NotContain(" - ");
    }
}
