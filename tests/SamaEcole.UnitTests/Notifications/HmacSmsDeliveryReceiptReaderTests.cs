using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SamaEcole.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SamaEcole.UnitTests.Notifications;

/// <summary>
/// Le webhook d'accusés de réception est une route PUBLIQUE (aucun JWT — c'est l'agrégateur qui
/// appelle). Sa signature est donc la seule chose qui empêche un tiers de déclarer « livré » un SMS
/// jamais parti, ou « échoué » un SMS livré pour faire recréditer des segments à volonté.
/// </summary>
public class HmacSmsDeliveryReceiptReaderTests
{
    private const string Secret = "secret_partage_de_test";

    private const string DeliveredBody =
        """{"results":[{"messageId":"abc-123","status":{"groupName":"DELIVERED"}}]}""";

    [Fact]
    public void Valid_Signature_Yields_Delivered_Receipt()
    {
        var reader = NewReader();

        reader.TryRead(DeliveredBody, Sign(DeliveredBody), out var receipts).Should().BeTrue();

        receipts.Should().ContainSingle();
        receipts[0].ProviderMessageId.Should().Be("abc-123");
        receipts[0].IsDelivered.Should().BeTrue();
    }

    /// <summary>Certains agrégateurs préfixent la valeur (« sha256=… ») : le préfixe ne doit pas gêner.</summary>
    [Fact]
    public void Prefixed_Signature_Is_Accepted()
    {
        NewReader().TryRead(DeliveredBody, $"sha256={Sign(DeliveredBody)}", out _).Should().BeTrue();
    }

    [Fact]
    public void Wrong_Signature_Is_Rejected()
    {
        NewReader().TryRead(DeliveredBody, Sign("un autre corps"), out var receipts).Should().BeFalse();
        receipts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_Signature_Is_Rejected(string? signature)
    {
        NewReader().TryRead(DeliveredBody, signature, out _).Should().BeFalse();
    }

    /// <summary>
    /// Échec SÛR par défaut : sans secret configuré, le webhook se ferme au lieu de s'ouvrir à tous.
    /// C'est le comportement inverse de celui qu'on obtient « par accident » en oubliant le test.
    /// </summary>
    [Fact]
    public void Unconfigured_Secret_Rejects_Even_A_Correctly_Signed_Body()
    {
        var reader = NewReader(secret: "REMPLACER");

        reader.TryRead(DeliveredBody, Sign(DeliveredBody), out _).Should().BeFalse();
    }

    /// <summary>
    /// PENDING est un état d'ACHEMINEMENT, pas un verdict : le traiter comme un échec marquerait
    /// « échoué » un SMS encore en route et recréditerait l'école à tort.
    /// </summary>
    [Fact]
    public void Pending_Status_Produces_No_Receipt()
    {
        const string body = """{"results":[{"messageId":"abc-123","status":{"groupName":"PENDING"}}]}""";

        NewReader().TryRead(body, Sign(body), out var receipts).Should().BeTrue();
        receipts.Should().BeEmpty();
    }

    [Fact]
    public void Undelivered_Status_Produces_A_Failed_Receipt()
    {
        const string body = """{"results":[{"messageId":"abc-123","status":{"groupName":"UNDELIVERABLE"}}]}""";

        NewReader().TryRead(body, Sign(body), out var receipts).Should().BeTrue();

        receipts.Should().ContainSingle();
        receipts[0].IsDelivered.Should().BeFalse();
        receipts[0].FailureReason.Should().Contain("UNDELIVERABLE");
    }

    [Fact]
    public void Malformed_Body_Is_Rejected_Rather_Than_Throwing()
    {
        const string body = "ceci n'est pas du JSON";

        NewReader().TryRead(body, Sign(body), out _).Should().BeFalse();
    }

    private static HmacSmsDeliveryReceiptReader NewReader(string secret = Secret) =>
        new(Options.Create(new SmsOptions { WebhookSecret = secret }),
            NullLogger<HmacSmsDeliveryReceiptReader>.Instance);

    private static string Sign(string body) =>
        Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(body)));
}
