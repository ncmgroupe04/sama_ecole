using FluentAssertions;
using SamaEcole.Application.Absences.Commands.CreateEarlyDeparture;
using SamaEcole.Application.Absences.Queries.GetEarlyDepartures;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;
using SamaEcole.Application.Finance.Commands.CreateTeacherHourRecord;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using SamaEcole.Application.Finance.Queries.GetTeacherHourRecords;
using SamaEcole.Application.VieScolaire.Commands.CreateParentSummons;
using SamaEcole.Application.VieScolaire.Queries.GetParentSummons;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Documents;

/// <summary>
/// Module Documents administratifs (cahier des charges élite, Phase 2) — les 4 nouvelles tables
/// tenant (EarlyDepartures, ParentSummons, FinancialCommitments, TeacherHourRecords) tiennent-elles
/// l'isolation, à la fois côté C# (Handlers, filtre EF) ET côté base (policy RLS, AGENTS.md règle #2) ?
///
/// <see cref="RlsCoverageTests"/> prouve déjà que chaque table a RLS ACTIVE + une policy. Ce fichier
/// va plus loin : il prouve que la policy FILTRE réellement (pas seulement qu'elle existe), et que
/// les Handlers rejettent toute référence croisée vers l'école d'un autre tenant.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DocumentsModuleIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000b");
    private static readonly Guid AnneeA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
    private static readonly Guid EleveB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");
    private static readonly Guid InscriptionA = Guid.Parse("11111111-0000-0000-0000-0000000000a1");
    private static readonly Guid InscriptionB = Guid.Parse("11111111-0000-0000-0000-0000000000b1");
    private static readonly Guid EnseignantA = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid EnseignantB = Guid.Parse("cccccccc-0000-0000-0000-00000000000b");
    private static readonly Guid ContratA = Guid.Parse("dddddddd-0000-0000-0000-00000000000a");
    private static readonly Guid ContratB = Guid.Parse("dddddddd-0000-0000-0000-00000000000b");

    private static readonly Guid SortieA = Guid.Parse("ffffffff-0000-0000-0000-00000000000a");
    private static readonly Guid SortieB = Guid.Parse("ffffffff-0000-0000-0000-00000000000b");
    private static readonly Guid ConvocationA = Guid.Parse("11111111-1111-0000-0000-00000000000a");
    private static readonly Guid ConvocationB = Guid.Parse("11111111-1111-0000-0000-00000000000b");
    private static readonly Guid EngagementA = Guid.Parse("22222222-2222-0000-0000-00000000000a");
    private static readonly Guid EngagementB = Guid.Parse("22222222-2222-0000-0000-00000000000b");
    private static readonly Guid HeureA = Guid.Parse("33333333-3333-0000-0000-00000000000a");
    private static readonly Guid HeureB = Guid.Parse("33333333-3333-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e", Level = "Collège", Capacity = 45 });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2025-2026", StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true });

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA, GuardianName = "Moussa Fall", GuardianPhone = "+221770000001" },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Ibrahima Ndiaye", BirthDate = new DateOnly(2014, 2, 10), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseB });

        owner.Enrollments.AddRange(
            new Enrollment { Id = InscriptionA, SchoolId = EcoleA, StudentId = EleveA, SchoolYearId = AnneeA, ClassroomId = ClasseA, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 100_000m, AmountPaid = 40_000m, ReceiptNumber = "REC-A-0001", EnrolledAt = DateTimeOffset.UtcNow },
            new Enrollment { Id = InscriptionB, SchoolId = EcoleB, StudentId = EleveB, SchoolYearId = AnneeB, ClassroomId = ClasseB, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 150_000m, AmountPaid = 0m, ReceiptNumber = "REC-B-0001", EnrolledAt = DateTimeOffset.UtcNow });

        owner.Teachers.AddRange(
            new Teacher { Id = EnseignantA, SchoolId = EcoleA, Matricule = "ENS-A-0001", FullName = "Fatou Ndiaye", Email = "fatou@ecole-a.sn", BirthDate = new DateOnly(1985, 4, 12) },
            new Teacher { Id = EnseignantB, SchoolId = EcoleB, Matricule = "ENS-B-0001", FullName = "Cheikh Diop", Email = "cheikh@ecole-b.sn", BirthDate = new DateOnly(1982, 6, 3) });

        owner.EmployeeContracts.AddRange(
            new EmployeeContract { Id = ContratA, SchoolId = EcoleA, TeacherId = EnseignantA, Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 5_000m, TransportAllowance = 0m },
            new EmployeeContract { Id = ContratB, SchoolId = EcoleB, TeacherId = EnseignantB, Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 6_000m, TransportAllowance = 0m });

        owner.EarlyDepartures.AddRange(
            new EarlyDeparture { Id = SortieA, SchoolId = EcoleA, StudentId = EleveA, Date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), DepartureTime = new TimeOnly(11, 0), Reason = "RDV médical" },
            new EarlyDeparture { Id = SortieB, SchoolId = EcoleB, StudentId = EleveB, Date = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), DepartureTime = new TimeOnly(11, 0), Reason = "RDV médical" });

        owner.ParentSummons.AddRange(
            new ParentSummons { Id = ConvocationA, SchoolId = EcoleA, StudentId = EleveA, ScheduledAt = DateTimeOffset.UtcNow, Reason = "Résultats en baisse" },
            new ParentSummons { Id = ConvocationB, SchoolId = EcoleB, StudentId = EleveB, ScheduledAt = DateTimeOffset.UtcNow, Reason = "Résultats en baisse" });

        owner.FinancialCommitments.AddRange(
            new FinancialCommitment { Id = EngagementA, SchoolId = EcoleA, EnrollmentId = InscriptionA, Amount = 60_000m, DueDate = new DateOnly(2026, 6, 30), Terms = "3 versements mensuels" },
            new FinancialCommitment { Id = EngagementB, SchoolId = EcoleB, EnrollmentId = InscriptionB, Amount = 150_000m, DueDate = new DateOnly(2026, 6, 30), Terms = "2 versements mensuels" });

        owner.TeacherHourRecords.AddRange(
            new TeacherHourRecord { Id = HeureA, SchoolId = EcoleA, EmployeeContractId = ContratA, Date = new DateOnly(2026, 3, 2), Hours = 3m },
            new TeacherHourRecord { Id = HeureB, SchoolId = EcoleB, EmployeeContractId = ContratB, Date = new DateOnly(2026, 3, 2), Hours = 4m });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ Billet de sortie (EarlyDeparture)

    [Fact]
    public async Task Creating_An_Early_Departure_For_Another_Schools_Student_Is_Rejected_As_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateEarlyDepartureCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateEarlyDepartureCommand { StudentId = EleveB, Date = DateTime.UtcNow, DepartureTime = new TimeOnly(10, 0), Reason = "Test" };
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Early_Departures_List_Never_Leaks_Another_Schools_Record()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetEarlyDeparturesQueryHandler(ctx);

        var result = await handler.Handle(new GetEarlyDeparturesQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.StudentFullName.Should().Be("Awa Fall");
        result.Should().NotContain(d => d.Id == SortieB, "la sortie anticipée de l'École B ne doit jamais apparaître");
    }

    // ------------------------------------------------------------------ Convocation parent (ParentSummons)

    [Fact]
    public async Task Creating_A_Parent_Summons_For_Another_Schools_Student_Is_Rejected_As_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateParentSummonsCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateParentSummonsCommand { StudentId = EleveB, ScheduledAt = DateTimeOffset.UtcNow, Reason = "Test" };
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Parent_Summons_List_Never_Leaks_Another_Schools_Record()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetParentSummonsQueryHandler(ctx);

        var result = await handler.Handle(new GetParentSummonsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.StudentFullName.Should().Be("Awa Fall");
        result.Should().NotContain(p => p.Id == ConvocationB, "la convocation de l'École B ne doit jamais apparaître");
    }

    // ------------------------------------------------------------------ Engagement financier (FinancialCommitment)

    [Fact]
    public async Task Creating_A_Financial_Commitment_For_Another_Schools_Enrollment_Is_Rejected_As_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateFinancialCommitmentCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateFinancialCommitmentCommand(InscriptionB, 10_000m, new DateOnly(2026, 6, 30), "Test");
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Reading_Another_Schools_Financial_Commitment_By_Id_Is_Invisible()
    {
        // Pas de 403 : l'engagement de l'École B est structurellement introuvable sous le tenant A,
        // exactement comme un reçu ou un certificat d'une autre école (RLS + filtre EF).
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetFinancialCommitmentQueryHandler(ctx);

        var act = async () => await handler.Handle(new GetFinancialCommitmentQuery(EngagementB), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ Fiche d'heures vacataires (TeacherHourRecord)

    [Fact]
    public async Task Creating_An_Hour_Record_For_Another_Schools_Contract_Is_Rejected_As_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateTeacherHourRecordCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateTeacherHourRecordCommand(ContratB, new DateOnly(2026, 3, 10), 2m, null);
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Hour_Records_List_Never_Leaks_Another_Schools_Record()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetTeacherHourRecordsQueryHandler(ctx);

        var result = await handler.Handle(new GetTeacherHourRecordsQuery(ContratA, null, null), CancellationToken.None);

        result.Should().ContainSingle().Which.Hours.Should().Be(3m);
    }

    // ------------------------------------------------------------------ Preuve RLS au niveau BASE (SQL brut, sans EF)

    [Theory]
    [InlineData("EarlyDepartures")]
    [InlineData("ParentSummons")]
    [InlineData("FinancialCommitments")]
    [InlineData("TeacherHourRecords")]
    public async Task RawSqlQuery_Should_Never_Return_Other_Schools_Row(string table)
    {
        // Sans EF ni filtre C# : si une ligne de l'École B remonte ici, c'est que la policy RLS ne
        // filtre pas réellement (RlsCoverageTests prouve seulement qu'elle EXISTE, pas qu'elle filtre).
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(*) FROM "{table}" WHERE "SchoolId" = @ecoleB;""";
        command.Parameters.AddWithValue("ecoleB", EcoleB);

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, $"la RLS doit masquer toute ligne de {table} appartenant à l'École B sous le tenant A");
    }

    [Fact]
    public async Task RawSqlQuery_Without_Tenant_Should_See_No_Row_At_All()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "EarlyDepartures";""";

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "sans tenant, current_setting('app.current_school_id') est vide : aucune ligne ne doit être visible");
    }
}

/// <summary>Fournit un SchoolId fixe, sans passer par le contexte HTTP — suffisant pour un handler appelé directement en test.</summary>
file sealed class FixedTenantProvider(Guid schoolId) : Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
