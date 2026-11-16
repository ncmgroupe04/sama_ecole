using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Notifications;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.IntegrationTests.Notifications;

/// <summary>
/// Relance d'impayé : WhatsApp d'abord, SMS en repli. Les gardes (formule, commutateur) priment sur
/// le canal, et un message accepté par Meta n'est jamais doublé d'un SMS.
/// </summary>
public class DuesReminderDispatcherTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Premium = Guid.Parse("66666666-0000-0000-0000-000000000001");
    private static readonly Guid Standard = Guid.Parse("66666666-0000-0000-0000-000000000002");
    private static readonly Guid AlertOff = Guid.Parse("66666666-0000-0000-0000-000000000003");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = Premium, Name = "Premium" },
            new School { Id = Standard, Name = "Standard" },
            new School { Id = AlertOff, Name = "Alerte coupée" });

        AddSchool(owner, Premium, SubscriptionPlan.Premium, duesReminder: true);
        AddSchool(owner, Standard, SubscriptionPlan.Standard, duesReminder: true);
        AddSchool(owner, AlertOff, SubscriptionPlan.Premium, duesReminder: false);

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static void AddSchool(Persistence.ApplicationDbContext owner, Guid id, SubscriptionPlan plan, bool duesReminder)
    {
        owner.Subscriptions.Add(new Subscription
        {
            SchoolId = id, Plan = plan, Status = SubscriptionStatus.Active, ExpiresAt = new DateOnly(2030, 1, 1)
        });
        owner.SchoolSettings.Add(new SchoolSettings
        {
            SchoolId = id, SmsCreditBalance = 100, SmsOnDuesReminder = duesReminder
        });
    }

    private async Task<(DuesReminderOutcome Outcome, ScriptedWhatsApp WhatsApp, CountingSms Sms)> RunAsync(
        Guid schoolId, WhatsAppSendResult whatsAppResult, string? phone = "+221770000001")
    {
        await using var context = _db.NewAppContext(schoolId);
        var whatsApp = new ScriptedWhatsApp(whatsAppResult);
        var sms = new CountingSms();
        var dispatcher = new DuesReminderDispatcher(
            context, whatsApp, sms, NullLogger<DuesReminderDispatcher>.Instance);

        var outcome = await dispatcher.DispatchAsync(
            new DuesReminderRequest(schoolId, phone, Guid.NewGuid(), "Awa Diop", 60_000m, "Rappel SMS"),
            CancellationToken.None);

        return (outcome, whatsApp, sms);
    }

    [Fact]
    public async Task A_Reminder_Accepted_By_WhatsApp_Is_Not_Doubled_By_An_Sms()
    {
        var (outcome, whatsApp, sms) = await RunAsync(Premium, WhatsAppSendResult.Sent);

        outcome.Channel.Should().Be(DuesReminderChannel.WhatsApp);
        sms.Calls.Should().Be(0);

        var template = whatsApp.Sent.Should().ContainSingle().Subject.Template!;
        template.Kind.Should().Be(WhatsAppTemplateKind.DuesReminder);
        template.BodyParameters.Should().Equal("Awa Diop", 60_000m.ToString("N0"));
        template.HeaderDocument.Should().BeNull("une relance n'a pas de pièce jointe");
    }

    [Theory]
    [InlineData(WhatsAppSendStatus.Simulated)]
    [InlineData(WhatsAppSendStatus.Failed)]
    public async Task Any_Non_Accepted_WhatsApp_Result_Falls_Back_To_Sms(WhatsAppSendStatus status)
    {
        var result = status == WhatsAppSendStatus.Simulated
            ? WhatsAppSendResult.Simulated
            : WhatsAppSendResult.Failed("jeton expiré", 190, 401);

        var (outcome, _, sms) = await RunAsync(Premium, result);

        outcome.Channel.Should().Be(DuesReminderChannel.Sms);
        sms.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_School_Outside_The_Plan_Never_Gets_A_WhatsApp_Reminder()
    {
        var (outcome, whatsApp, sms) = await RunAsync(Standard, WhatsAppSendResult.Sent);

        whatsApp.Sent.Should().BeEmpty("la formule prime sur le canal");
        sms.Calls.Should().Be(1, "la décision de refus appartient à SmsDispatcher, qui donne la raison");
        outcome.Channel.Should().Be(DuesReminderChannel.Sms);
    }

    [Fact]
    public async Task A_Disabled_Dues_Alert_Sends_No_WhatsApp()
    {
        var (_, whatsApp, _) = await RunAsync(AlertOff, WhatsAppSendResult.Sent);

        whatsApp.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_A_Phone_Number_Nothing_Is_Attempted_On_WhatsApp()
    {
        var (_, whatsApp, _) = await RunAsync(Premium, WhatsAppSendResult.Sent, phone: null);

        whatsApp.Sent.Should().BeEmpty();
    }

    private sealed class ScriptedWhatsApp(WhatsAppSendResult result) : IWhatsAppSender
    {
        public List<WhatsAppMessage> Sent { get; } = [];

        public Task<WhatsAppSendResult> SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.FromResult(result);
        }
    }

    /// <summary>Remplace SmsDispatcher : renvoie « mis en file » et compte les appels.</summary>
    private sealed class CountingSms : ISmsDispatcher
    {
        public int Calls { get; private set; }

        public Task<SmsDispatchOutcome> DispatchAsync(SmsDispatchRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(SmsDispatchOutcome.Queued(Guid.NewGuid()));
        }
    }
}
