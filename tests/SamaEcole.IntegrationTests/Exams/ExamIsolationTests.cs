using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using SamaEcole.Application.Exams.Queries.GetExamDossiers;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Exams;

/// <summary>
/// Module Examens officiels — les trois nouvelles tables tenant (exam_sessions, exam_dossiers,
/// exam_results) tiennent-elles l'isolation, à la fois côté C# (Handlers, filtre EF) ET côté base
/// (policy RLS, AGENTS.md règle #2) ? Même démarche que
/// <see cref="Inventory.InventoryIsolationTests"/> : <see cref="Multitenancy.RlsCoverageTests"/>
/// prouve seulement que la policy EXISTE, ce fichier prouve qu'elle FILTRE réellement.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ExamIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid AnneeA = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid AnneeB = Guid.Parse("aaaa2222-0000-0000-0000-000000000001");

    private static readonly Guid ClasseA = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");
    private static readonly Guid ClasseACollege = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid ClasseB = Guid.Parse("cccccccc-0000-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");
    private static readonly Guid EleveB = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");

    private static readonly Guid SessionA = Guid.Parse("55550001-0000-0000-0000-00000000000a");
    private static readonly Guid SessionA2 = Guid.Parse("55550002-0000-0000-0000-00000000000a");
    private static readonly Guid SessionB = Guid.Parse("55550001-0000-0000-0000-00000000000b");
    private static readonly Guid DossierA = Guid.Parse("66660001-0000-0000-0000-00000000000a");
    private static readonly Guid DossierB = Guid.Parse("66660001-0000-0000-0000-00000000000b");
    private static readonly Guid ResultatA = Guid.Parse("77770001-0000-0000-0000-00000000000a");
    private static readonly Guid ResultatB = Guid.Parse("77770001-0000-0000-0000-00000000000b");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeA, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseACollege, SchoolId = EcoleA, Name = "3e A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2 B", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-A-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-B-0001", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 8, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseB });

        owner.ExamSessions.AddRange(
            new ExamSession { Id = SessionA, SchoolId = EcoleA, SchoolYearId = AnneeA, ExamType = ExamType.CFEE },
            // Deuxième session BFEM de l'École A : sert à créer un dossier "propre" dans les tests qui
            // n'ont pas besoin de collisionner avec DossierA (déjà sur SessionA + EleveA).
            new ExamSession { Id = SessionA2, SchoolId = EcoleA, SchoolYearId = AnneeA, ExamType = ExamType.BFEM, Series = "G" },
            new ExamSession { Id = SessionB, SchoolId = EcoleB, SchoolYearId = AnneeB, ExamType = ExamType.CFEE });

        owner.ExamDossiers.AddRange(
            new ExamDossier { Id = DossierA, SchoolId = EcoleA, ExamSessionId = SessionA, StudentId = EleveA, ClassroomId = ClasseA },
            new ExamDossier { Id = DossierB, SchoolId = EcoleB, ExamSessionId = SessionB, StudentId = EleveB, ClassroomId = ClasseB });

        owner.ExamResults.AddRange(
            new ExamResult { Id = ResultatA, SchoolId = EcoleA, ExamDossierId = DossierA, IsAdmitted = true, DeliberatedOn = new DateOnly(2027, 7, 10) },
            new ExamResult { Id = ResultatB, SchoolId = EcoleB, ExamDossierId = DossierB, IsAdmitted = true, DeliberatedOn = new DateOnly(2027, 7, 10) });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ Isolation côté Handlers

    [Fact]
    public async Task Creating_A_Dossier_On_Another_Schools_Session_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateExamDossierCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateExamDossierCommand
        {
            ExamSessionId = SessionB,
            StudentId = EleveA,
            ClassroomId = ClasseA
        };

        var act = async () => await handler.Handle(command, CancellationToken.None);

        // La session de l'École B est structurellement introuvable sous le tenant A (Global Query
        // Filter + RLS), traduit en ValidationException (422) plutôt qu'en 500 issu de la FK.
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Creating_A_Dossier_For_Another_Schools_Student_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateExamDossierCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateExamDossierCommand
        {
            ExamSessionId = SessionA,
            StudentId = EleveB,
            ClassroomId = ClasseA
        };

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>(
            "un élève d'une autre école ne doit jamais pouvoir figurer sur un dossier d'examen de l'école A");
    }

    [Fact]
    public async Task Dossiers_List_Never_Leaks_Another_Schools_Dossier()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetExamDossiersQueryHandler(ctx);

        var result = await handler.Handle(new GetExamDossiersQuery(), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.StudentFullName.Should().Be("Awa Fall");
        result.Items.Should().NotContain(d => d.Id == DossierB, "le dossier de l'École B ne doit jamais apparaître");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Creating_A_Dossier_Never_Accepts_A_Client_Supplied_SchoolId()
    {
        // SchoolId vient toujours de ITenantProvider (AGENTS.md règle #10) — CreateExamDossierCommand
        // n'a d'ailleurs aucune propriété SchoolId à falsifier.
        // SessionA2 (BFEM/Collège), pas SessionA : EleveA a déjà un dossier sur SessionA (DossierA,
        // seedé dans InitializeAsync), et l'unicité (session, élève) rejetterait un second dossier.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateExamDossierCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var result = await handler.Handle(
            new CreateExamDossierCommand { ExamSessionId = SessionA2, StudentId = EleveA, ClassroomId = ClasseACollege },
            CancellationToken.None);

        var created = await ctx.ExamDossiers.FindAsync(result.Id);
        created!.SchoolId.Should().Be(EcoleA);
    }

    // ------------------------------------------------------------------ Preuve RLS au niveau BASE (SQL brut, sans EF)

    [Theory]
    [InlineData("exam_sessions")]
    [InlineData("exam_dossiers")]
    [InlineData("exam_results")]
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

    [Theory]
    [InlineData("exam_sessions")]
    [InlineData("exam_dossiers")]
    [InlineData("exam_results")]
    public async Task RawSqlQuery_Without_Tenant_Should_See_No_Row_At_All(string table)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT COUNT(*) FROM "{table}";""";

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "sans tenant positionné, la policy ne doit laisser passer AUCUNE ligne");
    }

    [Fact]
    public async Task Application_Role_Cannot_Delete_An_Exam_Dossier()
    {
        // Aucune suppression physique de donnée métier (AGENTS.md règle #6) : seul le soft delete
        // (IsDeleted) est permis, jamais un SQL DELETE — comme sur toute autre table tenant standard
        // (contrairement à stock_movements, exam_dossiers N'EST PAS append-only : ce test vérifie le
        // GRANT normal, pas un régime restreint).
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """DELETE FROM "exam_dossiers" WHERE "Id" = @id;""";
        command.Parameters.AddWithValue("id", DossierA);

        // Le rôle applicatif n'a reçu que SELECT/INSERT/UPDATE sur ce module (migration AddExamsModule) :
        // un DELETE brut doit être refusé par PostgreSQL, pas seulement omis par convention de code.
        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
