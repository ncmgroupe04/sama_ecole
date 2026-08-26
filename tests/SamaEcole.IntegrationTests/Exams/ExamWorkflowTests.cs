using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Exams.Commands.AssignExamCenter;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using SamaEcole.Application.Exams.Commands.CreateExamSession;
using SamaEcole.Application.Exams.Commands.RecordExamResult;
using SamaEcole.Application.Exams.Commands.TransmitExamDossier;
using SamaEcole.Application.Exams.Commands.UpdateExamDossier;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.IntegrationTests.Exams;

/// <summary>
/// Règles métier du module Examens officiels (Volume 1 §22, tickets JGK-J01/J02/J03/J07) qui ne
/// relèvent pas de l'isolation multi-tenant — voir <see cref="ExamIsolationTests"/> pour celle-ci.
/// </summary>
public class ExamWorkflowTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnneeA = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid ClassePrimaire = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ClasseLycee = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid Eleve = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid SessionCfee = Guid.Parse("55550001-0000-0000-0000-000000000001");

    private sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });
        owner.SchoolYears.Add(new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClassePrimaire, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseLycee, SchoolId = EcoleA, Name = "Terminale S2", Level = "Lycée", Cycle = CycleType.Lycee, Capacity = 40 });

        owner.Students.Add(
            new Student { Id = Eleve, SchoolId = EcoleA, Matricule = "ELEV-A-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClassePrimaire });

        owner.ExamSessions.Add(
            new ExamSession { Id = SessionCfee, SchoolId = EcoleA, SchoolYearId = AnneeA, ExamType = ExamType.CFEE });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ApplicationDbContext Ctx() => _db.NewAppContext(EcoleA);
    private static FixedTenantProvider Tenant() => new(EcoleA);

    [Fact]
    public async Task Dossier_Rejected_When_Classroom_Cycle_Does_Not_Match_Exam_Type()
    {
        await using var ctx = Ctx();
        var handler = new CreateExamDossierCommandHandler(ctx, Tenant());

        // Session CFEE (Primaire attendu) sur une classe de Lycée : incohérence de cycle.
        var act = async () => await handler.Handle(
            new CreateExamDossierCommand { ExamSessionId = SessionCfee, StudentId = Eleve, ClassroomId = ClasseLycee },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Dossier_Becomes_Complet_Only_When_Certificate_Present_And_Civil_Status_Conforming()
    {
        var dossierId = await CreateDossierAsync();

        await using var ctx = Ctx();
        var handler = new UpdateExamDossierCommandHandler(ctx);

        var rowVersion = await RowVersionAsync(ctx, dossierId);
        var result = await handler.Handle(
            new UpdateExamDossierCommand
            {
                Id = dossierId,
                BirthCertificatePresent = true,
                CivilStatusConforming = true,
                RowVersion = rowVersion
            },
            CancellationToken.None);

        result.Status.Should().Be(nameof(ExamDossierStatus.Complet));
    }

    [Fact]
    public async Task Dossier_Stays_Incomplet_When_Certificate_Missing()
    {
        var dossierId = await CreateDossierAsync();

        await using var ctx = Ctx();
        var handler = new UpdateExamDossierCommandHandler(ctx);

        var rowVersion = await RowVersionAsync(ctx, dossierId);
        var result = await handler.Handle(
            new UpdateExamDossierCommand
            {
                Id = dossierId,
                BirthCertificatePresent = false,
                CivilStatusConforming = true,
                RowVersion = rowVersion
            },
            CancellationToken.None);

        result.Status.Should().Be(nameof(ExamDossierStatus.Incomplet));
    }

    [Fact]
    public async Task Transmit_Rejected_When_Dossier_Is_Incomplet()
    {
        var dossierId = await CreateDossierAsync();

        await using var ctx = Ctx();
        var handler = new TransmitExamDossierCommandHandler(ctx, TimeProvider.System);

        var act = async () => await handler.Handle(new TransmitExamDossierCommand(dossierId), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>(
            "un dossier Incomplet ne doit jamais pouvoir être transmis (Volume 1 §22.3)");
    }

    [Fact]
    public async Task Transmit_Succeeds_Once_Dossier_Is_Complet()
    {
        var dossierId = await CreateDossierAsync();
        await MarkCompleteAsync(dossierId);

        await using var ctx = Ctx();
        var handler = new TransmitExamDossierCommandHandler(ctx, TimeProvider.System);

        var result = await handler.Handle(new TransmitExamDossierCommand(dossierId), CancellationToken.None);

        result.Status.Should().Be(nameof(ExamDossierStatus.Transmis));
    }

    [Fact]
    public async Task RecordResult_Rejected_Before_Transmission()
    {
        var dossierId = await CreateDossierAsync();
        await MarkCompleteAsync(dossierId); // Complet, mais pas encore Transmis.

        await using var ctx = Ctx();
        var handler = new RecordExamResultCommandHandler(ctx, Tenant());

        var act = async () => await handler.Handle(
            new RecordExamResultCommand { ExamDossierId = dossierId, IsAdmitted = true, DeliberatedOn = new DateOnly(2027, 7, 10) },
            CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>(
            "un résultat ne se saisit qu'après transmission du dossier (Volume 1 §22.6)");
    }

    [Fact]
    public async Task RecordResult_Rejects_A_Mention_On_A_Cfee_Session()
    {
        var dossierId = await CreateDossierAsync();
        await MarkCompleteAsync(dossierId);
        await TransmitAsync(dossierId);

        await using var ctx = Ctx();
        var handler = new RecordExamResultCommandHandler(ctx, Tenant());

        var act = async () => await handler.Handle(
            new RecordExamResultCommand
            {
                ExamDossierId = dossierId,
                IsAdmitted = true,
                Mention = ExamMention.Bien,
                DeliberatedOn = new DateOnly(2027, 7, 10)
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>("le CFEE n'attribue pas de mention (Volume 1 §22.6)");
    }

    [Fact]
    public async Task RecordResult_Moves_Dossier_To_Valide()
    {
        var dossierId = await CreateDossierAsync();
        await MarkCompleteAsync(dossierId);
        await TransmitAsync(dossierId);

        await using var ctx = Ctx();
        var handler = new RecordExamResultCommandHandler(ctx, Tenant());

        await handler.Handle(
            new RecordExamResultCommand { ExamDossierId = dossierId, IsAdmitted = true, DeliberatedOn = new DateOnly(2027, 7, 10) },
            CancellationToken.None);

        var dossier = await ctx.ExamDossiers.FindAsync(dossierId);
        dossier!.Status.Should().Be(ExamDossierStatus.Valide);
    }

    [Fact]
    public async Task AssignCenter_Generates_Candidate_Number_Inside_The_Transaction()
    {
        var dossierId = await CreateDossierAsync();

        await using var ctx = Ctx();
        var generator = new ExamCandidateNumberGenerator(ctx, TimeProvider.System);
        var handler = new AssignExamCenterCommandHandler(ctx, generator);

        var rowVersion = await RowVersionAsync(ctx, dossierId);
        var result = await handler.Handle(
            new AssignExamCenterCommand { Id = dossierId, ExamCenterName = "CEM Grand Dakar", RowVersion = rowVersion },
            CancellationToken.None);

        result.CandidateNumber.Should().Be("001");
        result.ExamCenterName.Should().Be("CEM Grand Dakar");
    }

    [Fact]
    public async Task Two_Cfee_Sessions_Without_Series_Are_Rejected_As_Duplicates()
    {
        // Cas précis qui a motivé le COALESCE de l'index unique (Volume 3 DDS §5.10) : Series est NULL
        // pour les deux, et NULL n'est jamais égal à NULL dans un index UNIQUE standard. SessionCfee
        // (CFEE, AnneeA, Series null) est déjà seedée dans InitializeAsync : un second CFEE sur la
        // même année doit donc être refusé dès ce seul appel.
        await using var ctx = Ctx();
        var handler = new CreateExamSessionCommandHandler(ctx, Tenant());

        var act = async () => await handler.Handle(
            new CreateExamSessionCommand { SchoolYearId = AnneeA, ExamType = ExamType.CFEE }, CancellationToken.None);

        await act.Should().ThrowAsync<DuplicateRecordException>();
    }

    // ------------------------------------------------------------------ Aides

    private async Task<Guid> CreateDossierAsync()
    {
        await using var ctx = Ctx();
        var handler = new CreateExamDossierCommandHandler(ctx, Tenant());

        var result = await handler.Handle(
            new CreateExamDossierCommand { ExamSessionId = SessionCfee, StudentId = Eleve, ClassroomId = ClassePrimaire },
            CancellationToken.None);

        return result.Id;
    }

    private async Task MarkCompleteAsync(Guid dossierId)
    {
        await using var ctx = Ctx();
        var handler = new UpdateExamDossierCommandHandler(ctx);
        var rowVersion = await RowVersionAsync(ctx, dossierId);

        await handler.Handle(
            new UpdateExamDossierCommand
            {
                Id = dossierId,
                BirthCertificatePresent = true,
                CivilStatusConforming = true,
                RowVersion = rowVersion
            },
            CancellationToken.None);
    }

    private async Task TransmitAsync(Guid dossierId)
    {
        await using var ctx = Ctx();
        var handler = new TransmitExamDossierCommandHandler(ctx, TimeProvider.System);
        await handler.Handle(new TransmitExamDossierCommand(dossierId), CancellationToken.None);
    }

    private static async Task<uint> RowVersionAsync(ApplicationDbContext ctx, Guid dossierId)
    {
        var dossier = await ctx.ExamDossiers.FindAsync(dossierId);
        return ctx.Entry(dossier!).Property<uint>("xmin").CurrentValue;
    }
}
