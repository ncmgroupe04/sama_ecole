using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Commands.SendReportCard;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using MediatR;
using Xunit;
using FluentAssertions;

namespace SamaEcole.IntegrationTests.ReportCards;

/// <summary>
/// Ticket JGK-G03 (envoi du bulletin) — le canal Email envoie désormais au véritable
/// <see cref="Student.GuardianEmail"/> de l'élève (avant ce correctif, il envoyait toujours à une
/// adresse fictive codée en dur "tutor@example.com", faute de ce champ sur Student), avec le même
/// garde-fou que le téléphone WhatsApp : un GuardianEmail absent bloque l'envoi Email/Both en amont.
///
/// Le mediator est ici un faux minimal : SendReportCardCommandHandler ne s'en sert QUE pour
/// régénérer le PDF (GetReportCardPdfQuery) — la mise en page réelle du bulletin est déjà couverte
/// par ReportCardDocumentTests/GetReportCardPdfTests, aucune raison de la refaire ici.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SendReportCardCommandTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseEcoleB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid EleveAvecTelephone = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
    private static readonly Guid EleveSansTelephone = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");
    private static readonly Guid EleveEcoleB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000c");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseEcoleB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 40 });

        owner.Students.AddRange(
            new Student
            {
                Id = EleveAvecTelephone, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall",
                BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe,
                GuardianName = "Ndèye Fall", GuardianPhone = "+221771234567", GuardianEmail = "ndeye.fall@example.com"
            },
            new Student
            {
                Id = EleveSansTelephone, SchoolId = EcoleA, Matricule = "ELEV-2026-0002", FullName = "Moussa Diop",
                BirthDate = new DateOnly(2015, 6, 12), BirthPlace = "Thiès", Gender = "M", ClassroomId = Classe,
                GuardianPhone = null
            },
            new Student
            {
                Id = EleveEcoleB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Élève École B",
                BirthDate = new DateOnly(2015, 1, 1), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseEcoleB,
                GuardianPhone = "+221770000000"
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static SendReportCardCommandHandler MakeHandler(
        IApplicationDbContext ctx, ITenantProvider tenantProvider,
        out FakeWhatsAppSender whatsApp, out FakeEmailSender email)
    {
        whatsApp = new FakeWhatsAppSender();
        email = new FakeEmailSender();
        return new SendReportCardCommandHandler(ctx, new FakeMediator(), whatsApp, email, tenantProvider);
    }

    [Fact]
    public async Task WhatsApp_Channel_Sends_To_The_Guardian_Phone_With_The_Report_Card_Attached()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out var email);

        await handler.Handle(new SendReportCardCommand(EleveAvecTelephone, Guid.NewGuid(), CommunicationChannel.WhatsApp), CancellationToken.None);

        whatsApp.LastMessage.Should().NotBeNull();
        whatsApp.LastMessage!.To.Should().Be("+221771234567");
        whatsApp.LastMessage.Attachments.Should().ContainSingle().Which.ContentType.Should().Be("application/pdf");
        email.LastMessage.Should().BeNull("le canal Email n'a pas été demandé");
    }

    [Fact]
    public async Task WhatsApp_Channel_Without_A_Guardian_Phone_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out var email);

        var act = async () => await handler.Handle(
            new SendReportCardCommand(EleveSansTelephone, Guid.NewGuid(), CommunicationChannel.WhatsApp), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        whatsApp.LastMessage.Should().BeNull("le numéro manquant doit bloquer l'envoi, jamais échouer silencieusement après coup");
        email.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task Both_Channel_Without_A_Guardian_Phone_Is_Rejected_Before_Sending_Anything()
    {
        // Comportement actuel du handler : le garde-fou WhatsApp bloque TOUT le canal "Both", y compris
        // la moitié Email qui n'aurait pourtant pas eu besoin du téléphone — aucun envoi partiel.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out var email);

        var act = async () => await handler.Handle(
            new SendReportCardCommand(EleveSansTelephone, Guid.NewGuid(), CommunicationChannel.Both), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        whatsApp.LastMessage.Should().BeNull();
        email.LastMessage.Should().BeNull("le canal Both échoue intégralement, jamais un envoi Email partiel");
    }

    [Fact]
    public async Task Email_Channel_Sends_To_The_Guardian_Email_With_The_Report_Card_Attached()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out var email);

        await handler.Handle(new SendReportCardCommand(EleveAvecTelephone, Guid.NewGuid(), CommunicationChannel.Email), CancellationToken.None);

        email.LastMessage.Should().NotBeNull();
        email.LastMessage!.To.Should().Be("ndeye.fall@example.com");
        email.LastMessage.Attachments.Should().ContainSingle().Which.ContentType.Should().Be("application/pdf");
        whatsApp.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task Email_Channel_Without_A_Guardian_Email_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out var email);

        var act = async () => await handler.Handle(
            new SendReportCardCommand(EleveSansTelephone, Guid.NewGuid(), CommunicationChannel.Email), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        email.LastMessage.Should().BeNull("l'e-mail manquant doit bloquer l'envoi, jamais échouer silencieusement après coup");
        whatsApp.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task Both_Channel_With_A_Guardian_Phone_Sends_On_Both_Channels()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out var email);

        await handler.Handle(new SendReportCardCommand(EleveAvecTelephone, Guid.NewGuid(), CommunicationChannel.Both), CancellationToken.None);

        whatsApp.LastMessage.Should().NotBeNull();
        email.LastMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task An_Unknown_Student_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out var email);

        var act = async () => await handler.Handle(
            new SendReportCardCommand(Guid.NewGuid(), Guid.NewGuid(), CommunicationChannel.WhatsApp), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        whatsApp.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task A_Student_From_Another_School_Is_Rejected_As_Not_Found()
    {
        // École A tente d'envoyer le bulletin d'un élève de l'École B : la vérification explicite
        // SchoolId == schoolId (en plus du filtre global EF + RLS) doit le traiter comme introuvable.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = MakeHandler(ctx, new FixedTenantProvider(EcoleA), out var whatsApp, out _);

        var act = async () => await handler.Handle(
            new SendReportCardCommand(EleveEcoleB, Guid.NewGuid(), CommunicationChannel.WhatsApp), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        whatsApp.LastMessage.Should().BeNull("un élève d'une autre école ne doit jamais recevoir/déclencher un envoi");
    }

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FakeWhatsAppSender : IWhatsAppSender
    {
        public WhatsAppMessage? LastMessage { get; private set; }

        public Task SendAsync(WhatsAppMessage message, CancellationToken cancellationToken)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public EmailMessage? LastMessage { get; private set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// SendReportCardCommandHandler ne s'en sert que pour un unique GetReportCardPdfQuery : un PDF
    /// canné suffit, la mise en page réelle est déjà testée ailleurs (ReportCardDocumentTests).
    /// </summary>
    private sealed class FakeMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetReportCardPdfQuery)
            {
                return Task.FromResult((TResponse)(object)new ReportCardPdfResult([1, 2, 3], "Bulletin-Test.pdf"));
            }

            throw new NotSupportedException($"FakeMediator ne sait pas répondre à {request.GetType().Name}.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new NotSupportedException();
    }
}
