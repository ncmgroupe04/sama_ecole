using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.VieScolaire.Commands.CreateParentSummons;
using SamaEcole.Application.VieScolaire.Queries.GetParentSummons;
using SamaEcole.Domain.Entities;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.VieScolaire;

/// <summary>
/// Convocations de parents (Volume 1 §18) — livrées depuis plusieurs sprints mais jusqu'ici couvertes
/// par le seul générateur de PDF. Même démarche que <see cref="Discipline.DisciplineIsolationTests"/>,
/// dont ce module partage le Handler-jumeau (même garde d'existence de l'élève, même absence de
/// SchoolId côté client) : l'isolation tient-elle réellement, côté C# ET côté base (RLS) ?
/// </summary>
[Trait("Category", "MultiTenant")]
public class ParentSummonsIsolationTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-3333-0000-0000-00000000000a");
    private static readonly Guid ClasseB = Guid.Parse("bbbbbbbb-3333-0000-0000-00000000000b");
    private static readonly Guid EleveA = Guid.Parse("eeeeeeee-3333-0000-0000-00000000000e");
    private static readonly Guid EleveB = Guid.Parse("ffffffff-3333-0000-0000-00000000000f");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

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
    public async Task Creating_A_Summons_For_Another_Schools_Student_Is_Rejected()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateParentSummonsCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var command = new CreateParentSummonsCommand
        {
            StudentId = EleveB,
            ScheduledAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero),
            Reason = "Absences répétées"
        };
        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Creating_A_Summons_Stamps_The_Tenants_SchoolId_Never_The_Clients()
    {
        // CreateParentSummonsCommand ne porte aucune propriété SchoolId à falsifier (AGENTS.md règle
        // #10) : le Handler la lit uniquement depuis ITenantProvider.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateParentSummonsCommandHandler(ctx, new FixedTenantProvider(EcoleA));

        var id = await handler.Handle(new CreateParentSummonsCommand
        {
            StudentId = EleveA,
            ScheduledAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero),
            Reason = "Comportement en classe"
        }, CancellationToken.None);

        var summons = await ctx.ParentSummons.FindAsync(id);
        summons!.SchoolId.Should().Be(EcoleA);
    }

    [Fact]
    public async Task Parent_Summons_List_Never_Leaks_Another_Schools_Row()
    {
        await using var owner = _db.NewOwnerContext();
        owner.ParentSummons.AddRange(
            new ParentSummons { Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = EleveA, ScheduledAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), Reason = "Convocation École A" },
            new ParentSummons { Id = Guid.NewGuid(), SchoolId = EcoleB, StudentId = EleveB, ScheduledAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), Reason = "Convocation École B" });
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetParentSummonsQueryHandler(ctx);

        var result = await handler.Handle(new GetParentSummonsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.Reason.Should().Be("Convocation École A");
        result.Should().NotContain(r => r.Reason == "Convocation École B");
    }

    // ------------------------------------------------------------------ Preuve RLS au niveau BASE (SQL brut, sans EF)

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_Other_Schools_Row()
    {
        await using var owner = _db.NewOwnerContext();
        owner.ParentSummons.AddRange(
            new ParentSummons { Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = EleveA, ScheduledAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), Reason = "École A" },
            new ParentSummons { Id = Guid.NewGuid(), SchoolId = EcoleB, StudentId = EleveB, ScheduledAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), Reason = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleA);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "ParentSummons" WHERE "SchoolId" = @ecoleB;""";
        command.Parameters.AddWithValue("ecoleB", EcoleB);

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "la RLS doit masquer toute ligne de ParentSummons appartenant à l'École B sous le tenant A");
    }

    [Fact]
    public async Task RawSqlQuery_Without_Tenant_Should_See_No_Row_At_All()
    {
        await using var owner = _db.NewOwnerContext();
        owner.ParentSummons.Add(
            new ParentSummons { Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = EleveA, ScheduledAt = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero), Reason = "École A" });
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId: null);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT COUNT(*) FROM "ParentSummons";""";

        var count = (long)(await command.ExecuteScalarAsync())!;

        count.Should().Be(0, "sans tenant, current_setting('app.current_school_id') est vide : aucune ligne ne doit être visible");
    }
}

/// <summary>Fournit un SchoolId fixe, sans passer par le contexte HTTP — suffisant pour un handler appelé directement en test.</summary>
file sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
