using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.EventHandlers;
using SamaEcole.Application.Enrollments.Events;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.IntegrationTests.Enrollments;

/// <summary>
/// Ticket JGK-E03 — notification e-mail des Directeurs actifs à chaque inscription. Trois garanties
/// verrouillées ici :
///   1. un Directeur actif de l'école reçoit bien l'e-mail ;
///   2. aucun Directeur actif trouvé -> pas d'exception (juste un avertissement journalisé) ;
///   3. un envoi qui échoue (SMTP transitoire) n'empêche ni les autres destinataires ni ne remonte —
///      <c>SmtpEmailSender.SendAsync</c> relance l'exception SMTP, le handler doit l'avaler.
/// </summary>
[Trait("Category", "MultiTenant")]
public class NotifyAdminOnStudentEnrolledEventHandlerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static StudentEnrolledEvent NewEvent(Guid schoolId, string studentFullName = "Awa Ndiaye", string matricule = "ELEV-2026-0001") =>
        new(schoolId, Guid.NewGuid(), Guid.NewGuid(), studentFullName, matricule, DateTimeOffset.UtcNow);

    private static User NewDirector(Guid schoolId, string email, EntityStatus status = EntityStatus.Active) => new()
    {
        SchoolId = schoolId,
        Email = email,
        PasswordHash = "hash",
        FullName = "Directeur Test",
        Role = Role.Directeur,
        Status = status
    };

    [Fact]
    public async Task An_Active_Director_Of_The_School_Receives_The_Enrollment_Email()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Users.Add(NewDirector(EcoleA, "directeur-a@ecole.test"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var emailSender = new FakeEmailSender();
        var handler = new NotifyAdminOnStudentEnrolledEventHandler(ctx, emailSender, NullLogger<NotifyAdminOnStudentEnrolledEventHandler>.Instance);

        await handler.Handle(NewEvent(EcoleA, "Awa Ndiaye", "ELEV-2026-0001"), CancellationToken.None);

        emailSender.SentMessages.Should().ContainSingle();
        var message = emailSender.SentMessages.Single();
        message.To.Should().Be("directeur-a@ecole.test");
        message.Subject.Should().Contain("Awa Ndiaye");
        message.Body.Should().Contain("Awa Ndiaye").And.Contain("ELEV-2026-0001");
    }

    [Fact]
    public async Task A_Director_From_Another_School_Never_Receives_The_Email()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Users.Add(NewDirector(EcoleB, "directeur-b@ecole.test"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var emailSender = new FakeEmailSender();
        var handler = new NotifyAdminOnStudentEnrolledEventHandler(ctx, emailSender, NullLogger<NotifyAdminOnStudentEnrolledEventHandler>.Instance);

        await handler.Handle(NewEvent(EcoleA), CancellationToken.None);

        emailSender.SentMessages.Should().BeEmpty("le Directeur d'une autre école n'a rien à voir avec cette inscription");
    }

    [Fact]
    public async Task A_Suspended_Director_Does_Not_Receive_The_Email()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Users.Add(NewDirector(EcoleA, "suspendu@ecole.test", EntityStatus.Suspended));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var emailSender = new FakeEmailSender();
        var handler = new NotifyAdminOnStudentEnrolledEventHandler(ctx, emailSender, NullLogger<NotifyAdminOnStudentEnrolledEventHandler>.Instance);

        await handler.Handle(NewEvent(EcoleA), CancellationToken.None);

        emailSender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task No_Active_Director_Does_Not_Throw()
    {
        // Aucun Directeur seedé pour EcoleA dans ce test : le handler doit se contenter de journaliser
        // un avertissement, jamais lever — une inscription réussie ne doit jamais provoquer d'erreur.
        await using var ctx = _db.NewAppContext(EcoleA);
        var emailSender = new FakeEmailSender();
        var handler = new NotifyAdminOnStudentEnrolledEventHandler(ctx, emailSender, NullLogger<NotifyAdminOnStudentEnrolledEventHandler>.Instance);

        var act = async () => await handler.Handle(NewEvent(EcoleA), CancellationToken.None);

        await act.Should().NotThrowAsync();
        emailSender.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Failing_Send_To_One_Director_Does_Not_Prevent_The_Others_And_Never_Rethrows()
    {
        // SmtpEmailSender.SendAsync relance l'exception SMTP (contrairement à SmsDispatcher) : le
        // handler doit l'avaler pour CHAQUE destinataire pris isolément, sans jamais faire échouer
        // l'inscription qui vient d'être enregistrée avec succès.
        await using var owner = _db.NewOwnerContext();
        owner.Users.AddRange(
            NewDirector(EcoleA, "echoue@ecole.test"),
            NewDirector(EcoleA, "reussit@ecole.test"));
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var emailSender = new FakeEmailSender(failingRecipient: "echoue@ecole.test");
        var handler = new NotifyAdminOnStudentEnrolledEventHandler(ctx, emailSender, NullLogger<NotifyAdminOnStudentEnrolledEventHandler>.Instance);

        var act = async () => await handler.Handle(NewEvent(EcoleA), CancellationToken.None);

        await act.Should().NotThrowAsync();
        emailSender.SentMessages.Should().ContainSingle(m => m.To == "reussit@ecole.test",
            "le destinataire en échec ne doit pas bloquer l'envoi aux autres Directeurs");
        emailSender.Attempts.Should().Contain("echoue@ecole.test", "l'envoi en échec a bien été TENTÉ, pas juste ignoré");
    }

    /// <summary>Simule SmtpEmailSender.SendAsync : relance pour <see cref="_failingRecipient"/>, sinon enregistre.</summary>
    private sealed class FakeEmailSender(string? failingRecipient = null) : IEmailSender
    {
        private readonly string? _failingRecipient = failingRecipient;

        public List<EmailMessage> SentMessages { get; } = [];
        public List<string> Attempts { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Attempts.Add(message.To);

            if (message.To == _failingRecipient)
            {
                throw new InvalidOperationException("Échec SMTP simulé.");
            }

            SentMessages.Add(message);
            return Task.CompletedTask;
        }
    }
}
