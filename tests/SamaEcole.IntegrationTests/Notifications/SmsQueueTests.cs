using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Notifications;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.IntegrationTests.Notifications;

/// <summary>
/// File d'attente des SMS, exercée contre un vrai PostgreSQL sous le RÔLE APPLICATIF — le seul où
/// les policies RLS mordent.
///
/// L'enjeu central de ces tests n'est pas la mécanique de file mais la TENSION entre deux exigences
/// contradictoires : le worker doit voir les messages de TOUTES les écoles, alors que la RLS est
/// précisément là pour qu'aucune session ne voie au-delà de la sienne. Le premier test fixe cette
/// tension noir sur blanc — un DbContext sans tenant ne voit RIEN, la fonction SECURITY DEFINER voit
/// la file. Si un jour quelqu'un « simplifie » le worker en le branchant sur un DbContext ordinaire,
/// c'est ce test qui doit tomber, et non les SMS en silence.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SmsQueueTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private const int InitialCredit = 100;

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        // Les deux écoles sont Premium ET ont l'alerte activée : sans cela, SmsDispatcher écarterait
        // les envois pour une raison sans rapport avec ce qui est testé ici.
        foreach (var schoolId in new[] { EcoleA, EcoleB })
        {
            owner.Subscriptions.Add(new Subscription
            {
                SchoolId = schoolId,
                Plan = SubscriptionPlan.Premium,
                Status = SubscriptionStatus.Active,
                ExpiresAt = new DateOnly(2027, 1, 1)
            });

            owner.SchoolSettings.Add(new SchoolSettings
            {
                SchoolId = schoolId,
                SmsCreditBalance = InitialCredit,
                SmsOnAttendanceAlert = true,
                SmsOnDuesReminder = true,
                SmsOnPaymentReceipt = true
            });
        }

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    /// <summary>
    /// La raison d'être des fonctions SECURITY DEFINER, prouvée dans les deux sens sur la MÊME
    /// connexion sans tenant : EF ne voit aucune ligne (la RLS fait son travail), claim_pending_sms
    /// les voit toutes (le worker peut travailler). Retirer la fonction ferait tourner un worker sur
    /// une file éternellement vide, sans la moindre erreur pour le signaler.
    /// </summary>
    [Fact]
    public async Task Tenantless_Context_Sees_Nothing_But_Claim_Function_Sees_Every_School()
    {
        await EnqueueAsync(EcoleA, "+221770000001", "Alerte École A");
        await EnqueueAsync(EcoleB, "+221770000002", "Alerte École B");

        await using var tenantless = _db.NewAppContext(schoolId: null);

        var visibleToEf = await tenantless.SmsMessages.IgnoreQueryFilters().CountAsync();
        visibleToEf.Should().Be(0, "la policy RLS masque toute ligne à une session sans tenant");

        var claimed = await NewQueueStore(tenantless)
            .ClaimPendingAsync(batchSize: 10, lease: TimeSpan.FromMinutes(2), CancellationToken.None);

        claimed.Should().HaveCount(2, "la fonction SECURITY DEFINER franchit la RLS, elle");
        claimed.Select(m => m.SchoolId).Should().BeEquivalentTo(new[] { EcoleA, EcoleB });
    }

    /// <summary>
    /// Le solde est débité à la MISE EN FILE, avant tout appel au fournisseur : c'est ce qui empêche
    /// une relance de masse d'accepter mille messages sur un solde de cent.
    /// </summary>
    [Fact]
    public async Task Dispatch_Queues_As_Pending_And_Debits_Balance_Immediately()
    {
        var outcome = await EnqueueAsync(EcoleA, "+221770000001", "Bonjour");

        outcome.IsQueued.Should().BeTrue();

        await using var owner = _db.NewOwnerContext();

        var message = await owner.SmsMessages.IgnoreQueryFilters().SingleAsync();
        message.Status.Should().Be(SmsDeliveryStatus.Pending);
        message.DispatchedAt.Should().BeNull("rien n'a encore été remis au fournisseur");
        message.NextAttemptAt.Should().NotBeNull("le message doit être éligible au prochain tour");

        (await BalanceOf(owner, EcoleA))
            .Should().BeLessThan(InitialCredit, "le débit a lieu dès la mise en file");
    }

    [Fact]
    public async Task Processor_Marks_Sent_And_Keeps_Provider_Reference()
    {
        await EnqueueAsync(EcoleA, "+221770000001", "Bonjour");

        var provider = new FakeSmsService();
        var processed = await RunOneTourAsync(provider);

        processed.Should().Be(1);
        provider.Sent.Should().ContainSingle().Which.To.Should().Be("+221770000001");

        await using var owner = _db.NewOwnerContext();
        var message = await owner.SmsMessages.IgnoreQueryFilters().SingleAsync();

        message.Status.Should().Be(SmsDeliveryStatus.Sent);
        message.DispatchedAt.Should().NotBeNull();
        message.ProviderMessageId.Should().Be(FakeSmsService.MessageId);
        message.NextAttemptAt.Should().BeNull("un message remis ne doit plus jamais être réclamé");
    }

    /// <summary>
    /// « Envoyé » n'est pas « reçu ». Seul l'accusé de réception fait passer à Delivered — c'est la
    /// seule preuve qu'un parent a été prévenu.
    /// </summary>
    [Fact]
    public async Task Delivery_Receipt_Promotes_Sent_To_Delivered_And_Is_Idempotent()
    {
        await EnqueueAsync(EcoleA, "+221770000001", "Bonjour");
        await RunOneTourAsync(new FakeSmsService());

        await using var tenantless = _db.NewAppContext(schoolId: null);
        var store = NewQueueStore(tenantless);

        var applied = await store.ApplyDeliveryReceiptAsync(
            FakeSmsService.MessageId, isDelivered: true, failureReason: null, CancellationToken.None);

        applied.Should().BeTrue();

        // Un agrégateur rejoue volontiers ses accusés : le second passage ne doit rien changer.
        var replayed = await store.ApplyDeliveryReceiptAsync(
            FakeSmsService.MessageId, isDelivered: true, failureReason: null, CancellationToken.None);

        replayed.Should().BeFalse("le message n'est plus au statut Sent, le rejeu est sans effet");

        await using var owner = _db.NewOwnerContext();
        var message = await owner.SmsMessages.IgnoreQueryFilters().SingleAsync();

        message.Status.Should().Be(SmsDeliveryStatus.Delivered);
        message.DeliveredAt.Should().NotBeNull();
    }

    /// <summary>
    /// Un échec isolé ne consomme pas le crédit et ne clôt pas le message : il le REPROGRAMME. Le
    /// solde ne doit bouger qu'à l'abandon définitif.
    /// </summary>
    [Fact]
    public async Task Transient_Failure_Reschedules_Without_Refunding()
    {
        await EnqueueAsync(EcoleA, "+221770000001", "Bonjour");

        await using var owner = _db.NewOwnerContext();
        var debited = await BalanceOf(owner, EcoleA);

        await RunOneTourAsync(new FakeSmsService { FailWith = "Opérateur injoignable." });

        var message = await owner.SmsMessages.IgnoreQueryFilters().AsNoTracking().SingleAsync();

        message.Status.Should().Be(SmsDeliveryStatus.Pending, "il reste des tentatives");
        message.AttemptCount.Should().Be(1);
        message.NextAttemptAt.Should().NotBeNull();

        (await BalanceOf(owner, EcoleA)).Should().Be(debited, "aucun recréditage avant l'abandon");
    }

    /// <summary>
    /// Tentatives épuisées : le message est classé en échec ET le solde est rendu. Sans ce
    /// recréditage, une panne de l'agrégateur ferait payer à l'école des SMS jamais partis.
    /// </summary>
    [Fact]
    public async Task Exhausted_Attempts_Fail_The_Message_And_Refund_The_Balance()
    {
        await EnqueueAsync(EcoleA, "+221770000001", "Bonjour");

        var provider = new FakeSmsService { FailWith = "Numéro invalide." };

        // MaxAttempts = 1 : le premier échec est donc déjà le dernier.
        await RunOneTourAsync(provider, new SmsQueueSettings { MaxAttempts = 1, Lease = TimeSpan.Zero });

        await using var owner = _db.NewOwnerContext();
        var message = await owner.SmsMessages.IgnoreQueryFilters().SingleAsync();

        message.Status.Should().Be(SmsDeliveryStatus.Failed);
        message.FailureReason.Should().Be("Numéro invalide.");
        message.NextAttemptAt.Should().BeNull();

        (await BalanceOf(owner, EcoleA)).Should().Be(InitialCredit, "le solde est intégralement rendu");
    }

    /// <summary>
    /// Le bail est ce qui empêche deux instances de l'application d'envoyer le MÊME SMS deux fois au
    /// parent. Une seconde réclamation immédiate ne doit donc rien retourner.
    /// </summary>
    [Fact]
    public async Task Claimed_Message_Is_Leased_Against_A_Concurrent_Worker()
    {
        await EnqueueAsync(EcoleA, "+221770000001", "Bonjour");

        await using var first = _db.NewAppContext(schoolId: null);
        await using var second = _db.NewAppContext(schoolId: null);

        var claimedByFirst = await NewQueueStore(first)
            .ClaimPendingAsync(10, TimeSpan.FromMinutes(2), CancellationToken.None);

        var claimedBySecond = await NewQueueStore(second)
            .ClaimPendingAsync(10, TimeSpan.FromMinutes(2), CancellationToken.None);

        claimedByFirst.Should().HaveCount(1);
        claimedBySecond.Should().BeEmpty("le bail du premier worker couvre encore le message");
    }

    // ── Utilitaires ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Passe par le VRAI SmsDispatcher, sous le rôle applicatif et avec le tenant de l'école : c'est
    /// lui qui porte les gardes (formule, commutateur, solde) et l'écriture en file.
    /// </summary>
    private async Task<SmsDispatchOutcome> EnqueueAsync(Guid schoolId, string recipient, string body)
    {
        await using var context = _db.NewAppContext(schoolId);

        var dispatcher = new SmsDispatcher(
            context, TimeProvider.System, NullLogger<SmsDispatcher>.Instance);

        return await dispatcher.DispatchAsync(
            new SmsDispatchRequest(schoolId, recipient, body, SmsTrigger.AttendanceAlert),
            CancellationToken.None);
    }

    /// <summary>Un tour de worker, sans tenant — exactement la situation de SmsQueueHostedService.</summary>
    private async Task<int> RunOneTourAsync(FakeSmsService provider, SmsQueueSettings? settings = null)
    {
        await using var context = _db.NewAppContext(schoolId: null);

        var processor = new SmsQueueProcessor(
            NewQueueStore(context),
            provider,
            settings ?? new SmsQueueSettings { Lease = TimeSpan.Zero },
            NullLogger<SmsQueueProcessor>.Instance);

        return await processor.ProcessOnceAsync(CancellationToken.None);
    }

    private static SmsQueueStore NewQueueStore(Persistence.ApplicationDbContext context) => new(context);

    private static async Task<int> BalanceOf(Persistence.ApplicationDbContext owner, Guid schoolId) =>
        await owner.SchoolSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => s.SmsCreditBalance)
            .SingleAsync();

    private sealed class FakeSmsService : ISmsService
    {
        public const string MessageId = "provider-msg-001";

        public string ProviderName => "Fake";

        /// <summary>Non nul : l'agrégateur refuse l'envoi (le contrat interdit de lever).</summary>
        public string? FailWith { get; init; }

        public List<SmsSendRequest> Sent { get; } = [];

        public Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request);

            return Task.FromResult(FailWith is null
                ? new SmsSendResult(true, MessageId, null, 1)
                : new SmsSendResult(false, null, FailWith, 1));
        }
    }
}
