using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Subscriptions;

/// <summary>
/// <c>tenant_subscriptions</c> tient-elle son isolation, ses bornes, sa reprise des écoles existantes et sa
/// réversibilité DANS LA BASE ? SQL brut sous le rôle applicatif pour la RLS (seule la policy fait foi),
/// et migration réelle aller-retour avec des données pour la reprise.
/// </summary>
[Trait("Category", "MultiTenant")]
public class TenantSubscriptionSchemaTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261005205214_AddDaaraHalqaAndHizbTracking";

    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("b1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("b2222222-2222-2222-2222-222222222222");

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ RLS / contraintes

    [Fact]
    public async Task Raw_Query_Should_Never_Return_Other_School_Rows()
    {
        await SeedSchoolsAsync();
        await InsertAsync(EcoleA, EcoleA);
        await InsertAsync(EcoleB, EcoleB);

        (await CountAsync(EcoleA)).Should().Be(1);
        (await CountAsync(EcoleB)).Should().Be(1);
        (await CountAsync(null)).Should().Be(0, "sans tenant dans la session, rien n'est visible");
    }

    [Fact]
    public async Task Writing_A_Subscription_Into_Another_School_Should_Be_Rejected()
    {
        await SeedSchoolsAsync();

        var act = async () => await InsertAsync(sessionSchool: EcoleA, rowSchool: EcoleB);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_Application_Role_Cannot_Physically_Delete_A_Subscription()
    {
        await SeedSchoolsAsync();
        await InsertAsync(EcoleA, EcoleA);

        var act = async () => await ExecuteAsync(EcoleA, "DELETE FROM tenant_subscriptions");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "aucune suppression physique (règle #6)");
    }

    [Fact]
    public async Task A_School_Cannot_Have_Two_Live_Subscriptions_But_A_Deleted_One_Does_Not_Block_A_New_One()
    {
        await SeedSchoolsAsync();
        await InsertAsync(EcoleA, EcoleA);

        var duplicate = async () => await InsertAsync(EcoleA, EcoleA);
        await duplicate.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        await ExecuteAsync(EcoleA, "UPDATE tenant_subscriptions SET \"IsDeleted\" = true");
        await InsertAsync(EcoleA, EcoleA);

        (await CountAsync(EcoleA)).Should().Be(2, "l'ancienne ligne supprimée logiquement subsiste, la nouvelle est la vivante");
    }

    [Theory]
    [InlineData(0, 0)]    // plafond nul
    [InlineData(-1, 10)]  // plafond négatif
    [InlineData(150, 149)] // tolérance inférieure au plafond
    public async Task Inconsistent_Limits_Are_Rejected_By_The_Database(int max, int soft)
    {
        await SeedSchoolsAsync();

        var act = async () => await InsertAsync(EcoleA, EcoleA, max, soft);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_Ef_Global_Query_Filter_Also_Hides_Other_School_Rows()
    {
        await SeedSchoolsAsync();
        await InsertAsync(EcoleA, EcoleA);
        await InsertAsync(EcoleB, EcoleB);

        await using var owner = _db.NewOwnerContext();
        await using var scoped = _db.NewAppContext(EcoleA);

        (await owner.TenantSubscriptions.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await scoped.TenantSubscriptions.AsNoTracking().Select(s => s.SchoolId).ToListAsync()).Should().Equal(EcoleA);
    }

    // ------------------------------------------------------------------ Reprise des écoles existantes

    [Fact]
    public async Task Migration_Backfills_Every_Existing_School_As_Active_And_Unlimited_And_Keeps_Chosen_Profiles()
    {
        // Les écoles existantes sont semées dans le schéma COURANT (le modèle EF y écrit toutes ses colonnes),
        // puis on RECULE avant la migration et on la rejoue sur ces données — exactement ce qui arrive en
        // production : Down supprime la table (et les colonnes ajoutées après), Up la recrée depuis school_settings.

        var cases = new Dictionary<string, (ProfileEtablissement? Profile, bool Pedagogy, bool Finance, bool Internat, bool Coran, ProfileType Expected)>
        {
            ["Simplifie"] = (ProfileEtablissement.Simplifie, false, true, false, false, ProfileType.ComptabiliteRapports),
            ["Elementaire"] = (ProfileEtablissement.ElementairePrimaire, true, true, false, false, ProfileType.Elementaire),
            ["General"] = (ProfileEtablissement.General, true, true, false, false, ProfileType.EnseignementGeneral),
            ["FrancoArabe"] = (ProfileEtablissement.FrancoArabe, true, true, false, true, ProfileType.FrancoArabe),
            ["Daara"] = (ProfileEtablissement.DaaraInternat, true, true, true, true, ProfileType.InternatDaara),
            // Aucun profil choisi : reçoit EnseignementGeneral, modules recopiés tels quels (ici modifiés à la main).
            ["SansProfil"] = (null, true, true, true, false, ProfileType.EnseignementGeneral),
        };

        var schoolIds = new Dictionary<string, Guid>();
        await using (var owner = _db.NewOwnerContext())
        {
            foreach (var (name, c) in cases)
            {
                var school = new School { Name = name };
                schoolIds[name] = school.Id;
                owner.Schools.Add(school);
                owner.SchoolSettings.Add(new SchoolSettings
                {
                    SchoolId = school.Id,
                    ProfileEtablissement = c.Profile,
                    IsPedagogyEnabled = c.Pedagogy,
                    IsFinanceEnabled = c.Finance,
                    IsInternatEnabled = c.Internat,
                    IsCoranModuleEnabled = c.Coran
                });
            }

            // École sans aucune ligne de réglages.
            var orphan = new School { Name = "SansReglages" };
            schoolIds["SansReglages"] = orphan.Id;
            owner.Schools.Add(orphan);

            await owner.SaveChangesAsync();
        }

        await MigrateToAsync(PreviousMigration);
        await MigrateToAsync(null);

        await using var verify = _db.NewOwnerContext();
        var rows = await verify.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        rows.Should().HaveCount(cases.Count + 1, "une ligne par école existante, ni plus ni moins");

        foreach (var (name, c) in cases)
        {
            var row = rows.Single(r => r.SchoolId == schoolIds[name]);
            row.ProfileType.Should().Be(c.Expected, name);
            row.IsPedagogyEnabled.Should().Be(c.Pedagogy, name);
            row.IsFinanceEnabled.Should().Be(c.Finance, name);
            row.IsInternatEnabled.Should().Be(c.Internat, name);
            row.IsCoranModuleEnabled.Should().Be(c.Coran, name);
        }

        var withoutSettings = rows.Single(r => r.SchoolId == schoolIds["SansReglages"]);
        withoutSettings.ProfileType.Should().Be(ProfileType.EnseignementGeneral);
        (withoutSettings.IsPedagogyEnabled, withoutSettings.IsFinanceEnabled, withoutSettings.IsInternatEnabled, withoutSettings.IsCoranModuleEnabled)
            .Should().Be((true, true, false, false), "valeurs par défaut des réglages : Pédagogie + Finance");

        rows.Should().OnlyContain(r =>
            r.Status == TenantSubscriptionStatus.Active
            && r.StudentQuotaTier == StudentQuotaTier.Tier4_Custom
            && r.MaxStudentLimit == int.MaxValue
            && r.SoftQuotaLimit == int.MaxValue
            && !r.IsDeleted);
    }

    [Fact]
    public async Task Migration_Is_Reversible_And_Can_Be_Replayed()
    {
        await MigrateToAsync(PreviousMigration);

        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.Add(new School { Name = "Ecole existante" });
            await owner.SaveChangesAsync();
        }

        (await TableExistsAsync()).Should().BeFalse("Down supprime la table");

        await MigrateToAsync(null);
        (await TableExistsAsync()).Should().BeTrue();

        // Rejouer Down puis Up ne doit ni échouer ni dupliquer de ligne.
        await MigrateToAsync(PreviousMigration);
        await MigrateToAsync(null);

        await using var verify = _db.NewOwnerContext();
        (await verify.TenantSubscriptions.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // ------------------------------------------------------------------ Service de lecture / quota

    [Fact]
    public async Task Quota_Status_Counts_Only_The_Current_School_And_Applies_The_Soft_Cap()
    {
        await SeedSchoolsAsync();
        await SeedStudentsAsync(EcoleA, count: 1);
        await SeedStudentsAsync(EcoleB, count: 5); // ne doit jamais compter pour A
        await InsertAsync(EcoleA, EcoleA, max: 1, soft: 2);

        (await QuotaAsync(EcoleA)).Should().BeEquivalentTo(
            new StudentQuotaStatus(1, 1, 2, StudentQuotaState.WithinQuota, true, StudentAdmissionDenial.None));

        await SeedStudentsAsync(EcoleA, count: 1, startAt: 10);

        // 2 élèves pour un plafond de 1 et une tolérance de 2 : en tolérance, mais un 3e serait refusé.
        (await QuotaAsync(EcoleA)).Should().BeEquivalentTo(
            new StudentQuotaStatus(2, 1, 2, StudentQuotaState.InTolerance, false, StudentAdmissionDenial.QuotaExceeded));
    }

    [Fact]
    public async Task A_School_Without_A_Subscription_Is_Refused_And_Cannot_See_Another_Schools_Subscription()
    {
        await SeedSchoolsAsync();
        await InsertAsync(EcoleB, EcoleB); // B a une souscription, A n'en a aucune

        var status = await QuotaAsync(EcoleA);

        status.CanAddStudent.Should().BeFalse();
        status.Denial.Should().Be(StudentAdmissionDenial.SubscriptionNotActive);

        await using var context = _db.NewAppContext(EcoleA);
        var service = new TenantSubscriptionService(context, new FixedTenant(EcoleA));

        (await service.GetCurrentAsync(CancellationToken.None)).Should().BeNull();
        (await service.IsModuleEnabledAsync(SchoolModule.Pedagogy, CancellationToken.None)).Should().BeFalse("fail-closed");
    }

    [Fact]
    public async Task A_Subscription_Pending_Onboarding_Blocks_Student_Creation_Whatever_The_Headcount()
    {
        await SeedSchoolsAsync();
        await InsertAsync(EcoleA, EcoleA, status: "PendingOnboarding");

        var status = await QuotaAsync(EcoleA);

        status.CanAddStudent.Should().BeFalse();
        status.Denial.Should().Be(StudentAdmissionDenial.SubscriptionNotActive);
    }

    [Fact]
    public async Task Service_Exposes_The_Subscription_And_Its_Module_Flags()
    {
        await SeedSchoolsAsync();
        await InsertAsync(EcoleA, EcoleA);

        await using var context = _db.NewAppContext(EcoleA);
        var service = new TenantSubscriptionService(context, new FixedTenant(EcoleA));

        var dto = await service.GetCurrentAsync(CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.SchoolId.Should().Be(EcoleA);
        dto.ProfileType.Should().Be("EnseignementGeneral");
        dto.Status.Should().Be("Active");
        (await service.IsModuleEnabledAsync(SchoolModule.Pedagogy, CancellationToken.None)).Should().BeTrue();
        (await service.IsModuleEnabledAsync(SchoolModule.Finance, CancellationToken.None)).Should().BeTrue();
        (await service.IsModuleEnabledAsync(SchoolModule.Internat, CancellationToken.None)).Should().BeFalse();
        (await service.IsModuleEnabledAsync(SchoolModule.Coran, CancellationToken.None)).Should().BeFalse();
    }

    // ------------------------------------------------------------------ Aides

    private async Task<StudentQuotaStatus> QuotaAsync(Guid school)
    {
        await using var context = _db.NewAppContext(school);
        return await new TenantSubscriptionService(context, new FixedTenant(school)).GetQuotaStatusAsync(1, CancellationToken.None);
    }

    private async Task SeedSchoolsAsync()
    {
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Ecole A" }, new School { Id = EcoleB, Name = "Ecole B" });
        await owner.SaveChangesAsync();
    }

    private async Task SeedStudentsAsync(Guid school, int count, int startAt = 1)
    {
        await using var owner = _db.NewOwnerContext();

        var classroom = new Classroom { SchoolId = school, Name = $"Classe {startAt}", Level = "CM2", Cycle = CycleType.Primaire, Capacity = 100 };
        owner.Classrooms.Add(classroom);

        for (var i = 0; i < count; i++)
        {
            owner.Students.Add(new Student
            {
                SchoolId = school,
                Matricule = $"M-{school.ToString()[..2]}-{startAt + i}",
                FullName = $"Eleve {startAt + i}",
                BirthDate = new DateOnly(2012, 1, 1),
                BirthPlace = "Dakar",
                Gender = "M",
                ClassroomId = classroom.Id
            });
        }

        await owner.SaveChangesAsync();
    }

    private async Task MigrateToAsync(string? targetMigration)
    {
        await using var owner = _db.NewOwnerContext();
        await owner.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private async Task<bool> TableExistsAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('public.tenant_subscriptions') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>INSERT brut sous le rôle applicatif ; <paramref name="sessionSchool"/> est le tenant de la session.</summary>
    private Task InsertAsync(
        Guid sessionSchool, Guid rowSchool, int max = 150, int soft = 160, string status = "Active") =>
        ExecuteAsync(sessionSchool,
            """
            INSERT INTO tenant_subscriptions
                ("Id", "SchoolId", "ProfileType", "StudentQuotaTier", "MaxStudentLimit", "SoftQuotaLimit", "Status",
                 "IsPedagogyEnabled", "IsFinanceEnabled", "IsInternatEnabled", "IsCoranModuleEnabled", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @school, 'EnseignementGeneral', 'Tier1_150', @max, @soft, @status,
                    true, true, false, false, now(), false)
            """,
            ("school", rowSchool), ("max", max), ("soft", soft), ("status", status));

    private async Task ExecuteAsync(Guid? sessionSchool, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? sessionSchool)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(sessionSchool);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM tenant_subscriptions";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
