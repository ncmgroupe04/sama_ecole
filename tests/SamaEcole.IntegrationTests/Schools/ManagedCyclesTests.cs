using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools.Commands.SetManagedCycles;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Schools;

/// <summary>
/// Réglage « cycles gérés » (<c>school_settings."ManagedCycles"</c>) sous la RLS réelle : reprise des écoles
/// existantes par la migration, réversibilité, et règle de désactivation (409 si le cycle contient des classes).
/// </summary>
[Trait("Category", "MultiTenant")]
public class ManagedCyclesTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261006101536_AddTenantSubscriptionAdminFunctions";

    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("d2222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Ecole A" }, new School { Id = EcoleB, Name = "Ecole B" });
        owner.SchoolSettings.AddRange(new SchoolSettings { SchoolId = EcoleA }, new SchoolSettings { SchoolId = EcoleB });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ Migration

    [Fact]
    public async Task A_New_School_Manages_Every_Cycle_By_Default()
    {
        await using var owner = _db.NewOwnerContext();

        var settings = await owner.SchoolSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == EcoleA);

        settings.ManagedCycles.Should().Be("Maternelle,Primaire,College,Lycee");
    }

    [Fact]
    public async Task The_Migration_Keeps_Elementary_Schools_On_Maternelle_And_Primaire_And_Gives_Every_Other_School_All_Cycles()
    {
        var profiles = new Dictionary<string, ProfileEtablissement?>
        {
            ["Elementaire"] = ProfileEtablissement.ElementairePrimaire,
            ["General"] = ProfileEtablissement.General,
            ["FrancoArabe"] = ProfileEtablissement.FrancoArabe,
            ["Daara"] = ProfileEtablissement.DaaraInternat,
            ["Simplifie"] = ProfileEtablissement.Simplifie,
            ["SansProfil"] = null
        };

        var ids = new Dictionary<string, Guid>();
        await using (var owner = _db.NewOwnerContext())
        {
            foreach (var (name, profile) in profiles)
            {
                var school = new School { Name = name };
                ids[name] = school.Id;
                owner.Schools.Add(school);
                owner.SchoolSettings.Add(new SchoolSettings { SchoolId = school.Id, ProfileEtablissement = profile });
            }

            await owner.SaveChangesAsync();
        }

        // On RECULE avant la migration (la colonne disparaît), puis on la rejoue sur ces données existantes :
        // c'est exactement ce qui arrive en production.
        await MigrateToAsync(PreviousMigration);
        (await ColumnExistsAsync()).Should().BeFalse("Down supprime la colonne");

        await MigrateToAsync(null);
        (await ColumnExistsAsync()).Should().BeTrue();

        await using var verify = _db.NewOwnerContext();
        var settings = await verify.SchoolSettings.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        settings.Single(s => s.SchoolId == ids["Elementaire"]).ManagedCycles
            .Should().Be("Maternelle,Primaire", "ces écoles sont déjà restreintes par le profil : on préserve leur affichage");

        foreach (var name in profiles.Keys.Where(n => n != "Elementaire"))
        {
            settings.Single(s => s.SchoolId == ids[name]).ManagedCycles
                .Should().Be("Maternelle,Primaire,College,Lycee", name);
        }

        settings.Single(s => s.SchoolId == EcoleA).ManagedCycles.Should().Be("Maternelle,Primaire,College,Lycee");
    }

    [Fact]
    public async Task The_Database_Refuses_An_Empty_Cycle_List()
    {
        await using var owner = _db.NewOwnerContext();

        var act = async () => await owner.Database.ExecuteSqlRawAsync(
            """UPDATE school_settings SET "ManagedCycles" = '' WHERE "SchoolId" = {0}""", EcoleA);

        await act.Should().ThrowAsync<Exception>().Where(e => e.ToString().Contains("CK_school_settings_managed_cycles_not_empty"));
    }

    // ------------------------------------------------------------------ Commande

    [Fact]
    public async Task Removing_A_Cycle_Without_Classes_Succeeds_And_Is_Stored_In_Canonical_Order()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var dto = await NewHandler(db, EcoleA).Handle(new SetManagedCyclesCommand(["Primaire", "Maternelle"]), CancellationToken.None);

        dto.ManagedCycles.Should().Equal("Maternelle", "Primaire");

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == EcoleA))
            .ManagedCycles.Should().Be("Maternelle,Primaire");
    }

    [Fact]
    public async Task Removing_A_Cycle_That_Still_Has_Live_Classrooms_Is_Refused_With_The_Details_And_Writes_Nothing()
    {
        await SeedClassroomsAsync(EcoleA, (CycleType.College, 2), (CycleType.Lycee, 1), (CycleType.Primaire, 3));

        await using var db = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(db, EcoleA).Handle(
            new SetManagedCyclesCommand(["Maternelle", "Primaire"]), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        ex.Code.Should().Be("CYCLE_HAS_CLASSROOMS");
        ex.Message.Should().Contain("Collège compte 2 classes").And.Contain("Lycée compte 1 classe.");
        ex.Details.Should().BeEquivalentTo(new[]
        {
            new { cycle = "College", classroomCount = 2 },
            new { cycle = "Lycee", classroomCount = 1 }
        });

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == EcoleA))
            .ManagedCycles.Should().Be("Maternelle,Primaire,College,Lycee", "le refus n'écrit rien");
    }

    [Fact]
    public async Task A_Cycle_Can_Be_Removed_Once_Its_Classrooms_Are_Soft_Deleted()
    {
        await SeedClassroomsAsync(EcoleA, (CycleType.Lycee, 1));
        await using (var owner = _db.NewOwnerContext())
        {
            foreach (var classroom in await owner.Classrooms.IgnoreQueryFilters().Where(c => c.SchoolId == EcoleA).ToListAsync())
            {
                classroom.SoftDelete("test");
            }

            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(EcoleA);
        var dto = await NewHandler(db, EcoleA).Handle(
            new SetManagedCyclesCommand(["Maternelle", "Primaire", "College"]), CancellationToken.None);

        dto.ManagedCycles.Should().Equal("Maternelle", "Primaire", "College");
    }

    [Fact]
    public async Task Another_Schools_Classrooms_Never_Block_This_School()
    {
        await SeedClassroomsAsync(EcoleB, (CycleType.Lycee, 4));

        await using var db = _db.NewAppContext(EcoleA);
        var dto = await NewHandler(db, EcoleA).Handle(new SetManagedCyclesCommand(["Primaire"]), CancellationToken.None);

        dto.ManagedCycles.Should().Equal("Primaire");

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == EcoleB))
            .ManagedCycles.Should().Be("Maternelle,Primaire,College,Lycee", "l'autre école n'a pas bougé");
    }

    [Fact]
    public async Task Adding_A_Cycle_Is_Never_Refused_Even_When_Classrooms_Exist()
    {
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await NewHandler(db, EcoleA).Handle(new SetManagedCyclesCommand(["Primaire"]), CancellationToken.None);
        }

        await SeedClassroomsAsync(EcoleA, (CycleType.Primaire, 5));

        await using var again = _db.NewAppContext(EcoleA);
        var dto = await NewHandler(again, EcoleA).Handle(new SetManagedCyclesCommand(["Primaire", "College"]), CancellationToken.None);

        dto.ManagedCycles.Should().Equal("Primaire", "College");
    }

    [Fact]
    public async Task Keeping_The_Same_Cycles_Is_Idempotent()
    {
        await SeedClassroomsAsync(EcoleA, (CycleType.Lycee, 1));

        await using var db = _db.NewAppContext(EcoleA);
        var dto = await NewHandler(db, EcoleA).Handle(
            new SetManagedCyclesCommand(["Lycee", "College", "Primaire", "Maternelle"]), CancellationToken.None);

        dto.ManagedCycles.Should().Equal("Maternelle", "Primaire", "College", "Lycee");
    }

    [Fact]
    public async Task A_School_Without_A_Settings_Row_Gets_One()
    {
        var orphan = Guid.NewGuid();
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.Add(new School { Id = orphan, Name = "Sans réglages" });
            await owner.SaveChangesAsync();
        }

        await using var db = _db.NewAppContext(orphan);
        var dto = await NewHandler(db, orphan).Handle(new SetManagedCyclesCommand(["Primaire"]), CancellationToken.None);

        dto.ManagedCycles.Should().Equal("Primaire");
    }

    // ------------------------------------------------------------------ Aides

    private async Task SeedClassroomsAsync(Guid school, params (CycleType Cycle, int Count)[] groups)
    {
        await using var owner = _db.NewOwnerContext();

        foreach (var (cycle, count) in groups)
        {
            for (var i = 0; i < count; i++)
            {
                owner.Classrooms.Add(new Classroom
                {
                    SchoolId = school,
                    Name = $"{cycle}-{Guid.NewGuid():N}"[..14],
                    Level = cycle switch { CycleType.Lycee => "Lycée", CycleType.College => "Collège", CycleType.Maternelle => "Maternelle", _ => "Primaire" },
                    Cycle = cycle,
                    Capacity = 40
                });
            }
        }

        await owner.SaveChangesAsync();
    }

    private async Task MigrateToAsync(string? targetMigration)
    {
        await using var owner = _db.NewOwnerContext();
        await owner.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private async Task<bool> ColumnExistsAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (SELECT 1 FROM information_schema.columns
                            WHERE table_name = 'school_settings' AND column_name = 'ManagedCycles')
            """;
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static SetManagedCyclesCommandHandler NewHandler(ApplicationDbContext db, Guid school) =>
        new(db, new FixedTenant(school), NullLogger<SetManagedCyclesCommandHandler>.Instance);

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
