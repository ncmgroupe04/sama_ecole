using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Discipline.Commands.CreateDisciplineRecord;
using SamaEcole.Application.Discipline.Queries.GetDisciplineRecords;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Discipline;

/// <summary>
/// Module Discipline & Convocations (Volume 1 §18) — livré depuis plusieurs sprints mais jusqu'ici
/// couvert par un seul test (le générateur de PDF du PV). Même démarche que
/// <see cref="Buildings.BuildingsIsolationTests"/> : l'isolation tient-elle réellement, côté C#
/// (Global Query Filter, Handler) ET côté base (policy RLS, AGENTS.md règle #2) ?
/// </summary>
[Trait("Category", "MultiTenant")]
public class DisciplineIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-2222-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-2222-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("eeeeeeee-2222-0000-0000-00000000000e");
    private static readonly Guid EleveB = Guid.Parse("ffffffff-2222-0000-0000-00000000000f");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        // Jeu de données posé par le PROPRIÉTAIRE (exempté de RLS) : de quoi écrire un signalement de
        // discipline VALIDE dans chaque école. L'élève référencé est réel, sans quoi l'insertion
        // échouerait sur une clé étrangère (23503) et non sur la RLS.
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "6e B", Level = "Collège", Capacity = 45 });

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = EcoleA, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseA },
            new Student { Id = EleveB, SchoolId = EcoleB, Matricule = "ELEV-2026-0001", FullName = "Modou Diop", BirthDate = new DateOnly(2014, 8, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseB });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Creating_A_Discipline_Record_For_Another_Schools_Student_Is_Rejected()
    {
        // Le Handler cherche l'élève via _context.Students (filtré par tenant) : un élève d'une autre
        // école est structurellement INTROUVABLE, jamais un 500 issu d'une contrainte étrangère.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateDisciplineRecordCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateDisciplineRecordCommand
        {
            StudentId = EleveB,
            Date = new DateTime(2026, 9, 3),
            Type = DisciplineType.Avertissement,
            Reason = "Bagarre dans la cour"
        };
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Creating_A_Discipline_Record_Stamps_The_Tenants_SchoolId_Never_The_Clients()
    {
        // CreateDisciplineRecordCommand ne porte aucune propriété SchoolId à falsifier (AGENTS.md
        // règle #10) : le Handler la lit uniquement depuis ITenantProvider.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateDisciplineRecordCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new CreateDisciplineRecordCommand
        {
            StudentId = EleveA,
            Date = new DateTime(2026, 9, 3),
            Type = DisciplineType.Avertissement,
            Reason = "Retard répété"
        }, CancellationToken.None);

        var record = await ctx.DisciplineRecords.FindAsync(id);
        record!.SchoolId.Should().Be(EcoleA);
    }

    [Fact]
    public async Task Discipline_Records_List_Never_Leaks_Another_Schools_Record()
    {
        await using var owner = _db.NewOwnerContext();
        owner.DisciplineRecords.AddRange(
            new DisciplineRecord { Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = EleveA, Date = new DateTime(2026, 9, 1), Type = DisciplineType.Avertissement, Reason = "Signalement École A" },
            new DisciplineRecord { Id = Guid.NewGuid(), SchoolId = EcoleB, StudentId = EleveB, Date = new DateTime(2026, 9, 1), Type = DisciplineType.Exclusion, Reason = "Signalement École B" });
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetDisciplineRecordsQueryHandler(ctx);

        var result = await handler.Handle(new GetDisciplineRecordsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.Reason.Should().Be("Signalement École A");
        result.Should().NotContain(r => r.Reason == "Signalement École B");
    }

    // ------------------------------------------------------------------ Preuve RLS au niveau BASE (SQL brut, sans EF)

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_Schools_Row()
    {
        await using var owner = _db.NewOwnerContext();
        owner.DisciplineRecords.AddRange(
            new DisciplineRecord { Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = EleveA, Date = new DateTime(2026, 9, 1), Type = DisciplineType.Avertissement, Reason = "École A" },
            new DisciplineRecord { Id = Guid.NewGuid(), SchoolId = EcoleB, StudentId = EleveB, Date = new DateTime(2026, 9, 1), Type = DisciplineType.Exclusion, Reason = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);

        // Sans EF ni filtre C# : si une ligne de l'École B remonte ici, c'est que la policy RLS ne
        // filtre pas réellement (RlsCoverageTests prouve seulement qu'elle EXISTE, pas qu'elle filtre).
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "DisciplineRecords" WHERE "SchoolId" = @ecoleB;""";
        command.Parameters.AddWithValue("ecoleB", EcoleB);

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "la RLS doit masquer toute ligne de DisciplineRecords appartenant à l'École B sous le tenant A");
    }

    [Fact]
    public async Task RawSqlQuery_Without_Tenant_Should_See_No_Row_At_All()
    {
        await using var owner = _db.NewOwnerContext();
        owner.DisciplineRecords.Add(
            new DisciplineRecord { Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = EleveA, Date = new DateTime(2026, 9, 1), Type = DisciplineType.Avertissement, Reason = "École A" });
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "DisciplineRecords";""";

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "sans tenant, current_setting('app.current_school_id') est vide : aucune ligne ne doit être visible");
    }
}

/// <summary>Fournit un SchoolId fixe, sans passer par le contexte HTTP — suffisant pour un handler appelé directement en test.</summary>
file sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
