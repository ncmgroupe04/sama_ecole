using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions.Commands.SelectProfile;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Subscriptions;

/// <summary>
/// Création de la souscription commerciale d'une école neuve (fonction SECURITY DEFINER gardée) et sortie de
/// l'Onboarding par le Directeur (SelectProfileCommand), le tout sous le rôle applicatif — seul à subir la RLS.
/// </summary>
[Trait("Category", "MultiTenant")]
public class TenantSubscriptionProvisioningTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("c2222222-2222-2222-2222-222222222222");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "Ecole A" }, new School { Id = EcoleB, Name = "Ecole B" });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // ------------------------------------------------------------------ Provisionnement

    [Fact]
    public async Task A_New_School_Is_Provisioned_Pending_Onboarding_With_All_Modules_Off()
    {
        // Session SANS tenant : exactement la situation d'un Super Admin qui approuve une demande.
        await using var context = _db.NewAppContext(null);

        var id = await _db.NewProvisioningStore(context).CreateInitialTenantSubscriptionAsync(EcoleA, CancellationToken.None);

        id.Should().NotBeNull();

        await using var owner = _db.NewOwnerContext();
        var row = await owner.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.SchoolId == EcoleA);
        row.Id.Should().Be(id!.Value);
        row.Status.Should().Be(TenantSubscriptionStatus.PendingOnboarding);
        (row.IsPedagogyEnabled, row.IsFinanceEnabled, row.IsInternatEnabled, row.IsCoranModuleEnabled)
            .Should().Be((false, false, false, false));
        row.MaxStudentLimit.Should().BePositive("les CHECK exigent un plafond positif même pour la ligne provisoire");
    }

    [Fact]
    public async Task Provisioning_Refuses_A_School_That_Already_Has_A_Subscription()
    {
        await using var context = _db.NewAppContext(null);
        var store = _db.NewProvisioningStore(context);

        (await store.CreateInitialTenantSubscriptionAsync(EcoleA, CancellationToken.None)).Should().NotBeNull();
        (await store.CreateInitialTenantSubscriptionAsync(EcoleA, CancellationToken.None))
            .Should().BeNull("garde anti-escalade : la fonction n'amorce que des souscriptions vierges");

        await using var owner = _db.NewOwnerContext();
        (await owner.TenantSubscriptions.IgnoreQueryFilters().CountAsync(s => s.SchoolId == EcoleA)).Should().Be(1);
    }

    [Fact]
    public async Task Without_The_Function_The_Application_Role_Could_Not_Insert_Outside_A_Tenant_Session()
    {
        // Contraste : c'est bien la fonction SECURITY DEFINER, et elle seule, qui ouvre cette porte.
        await using var connection = await _db.OpenRawAppConnectionAsync(null);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO tenant_subscriptions
                ("Id", "SchoolId", "ProfileType", "StudentQuotaTier", "MaxStudentLimit", "SoftQuotaLimit", "Status",
                 "IsPedagogyEnabled", "IsFinanceEnabled", "IsInternatEnabled", "IsCoranModuleEnabled", "CreatedAt", "IsDeleted")
            VALUES (gen_random_uuid(), @school, 'EnseignementGeneral', 'Tier1_150', 150, 160, 'Active',
                    true, true, false, false, now(), false)
            """;
        command.Parameters.AddWithValue("school", EcoleA);

        var act = async () => await command.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    // ------------------------------------------------------------------ Sortie de l'Onboarding

    [Fact]
    public async Task Selecting_A_Profile_Activates_The_Subscription_And_Aligns_The_School_Settings()
    {
        await ProvisionAsync(EcoleA);
        await ProvisionAsync(EcoleB);

        await using (var context = _db.NewAppContext(EcoleA))
        {
            var dto = await NewHandler(context, EcoleA)
                .Handle(new SelectProfileCommand(ProfileType.InternatDaara, StudentQuotaTier.Tier3_800), CancellationToken.None);

            dto.Status.Should().Be("Active");
            (dto.MaxStudentLimit, dto.SoftQuotaLimit).Should().Be((800, 830));
        }

        await using var owner = _db.NewOwnerContext();
        var a = await owner.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.SchoolId == EcoleA);
        a.ProfileType.Should().Be(ProfileType.InternatDaara);
        a.StudentQuotaTier.Should().Be(StudentQuotaTier.Tier3_800);
        (a.IsPedagogyEnabled, a.IsFinanceEnabled, a.IsInternatEnabled, a.IsCoranModuleEnabled).Should().Be((true, true, true, true));

        var settings = await owner.SchoolSettings.IgnoreQueryFilters().SingleAsync(s => s.SchoolId == EcoleA);
        settings.ProfileEtablissement.Should().Be(ProfileEtablissement.DaaraInternat);
        (settings.IsInternatEnabled, settings.IsCoranModuleEnabled).Should().Be((true, true));

        // L'autre école n'a pas bougé.
        var b = await owner.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.SchoolId == EcoleB);
        b.Status.Should().Be(TenantSubscriptionStatus.PendingOnboarding);
    }

    [Theory]
    [InlineData(ProfileType.Elementaire, "Maternelle,Primaire")]
    [InlineData(ProfileType.InternatDaara, "Maternelle,Primaire,College,Lycee")]
    [InlineData(ProfileType.FrancoArabe, "Maternelle,Primaire,College,Lycee")]
    [InlineData(ProfileType.EnseignementGeneral, "Maternelle,Primaire,College,Lycee")]
    [InlineData(ProfileType.ComptabiliteRapports, "Maternelle,Primaire,College,Lycee")]
    public async Task Selecting_A_Profile_Initializes_The_Managed_Cycles(ProfileType profile, string expected)
    {
        await ProvisionAsync(EcoleA);

        await using (var context = _db.NewAppContext(EcoleA))
        {
            await NewHandler(context, EcoleA).Handle(new SelectProfileCommand(profile, StudentQuotaTier.Tier1_150), CancellationToken.None);
        }

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolSettings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.SchoolId == EcoleA))
            .ManagedCycles.Should().Be(expected);
    }

    [Fact]
    public async Task Selecting_A_Profile_Twice_Is_Refused_And_Keeps_The_First_Choice()
    {
        await ProvisionAsync(EcoleA);

        await using (var context = _db.NewAppContext(EcoleA))
        {
            await NewHandler(context, EcoleA)
                .Handle(new SelectProfileCommand(ProfileType.Elementaire, StudentQuotaTier.Tier2_400), CancellationToken.None);
        }

        await using var again = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(again, EcoleA)
            .Handle(new SelectProfileCommand(ProfileType.ComptabiliteRapports, StudentQuotaTier.Tier1_150), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ONBOARDING_ALREADY_COMPLETED");

        await using var owner = _db.NewOwnerContext();
        var row = await owner.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.SchoolId == EcoleA);
        (row.ProfileType, row.StudentQuotaTier).Should().Be((ProfileType.Elementaire, StudentQuotaTier.Tier2_400));
    }

    [Fact]
    public async Task A_School_Cannot_Select_A_Profile_On_Behalf_Of_Another_School()
    {
        // Seule l'école B est provisionnée : A n'a aucune ligne et la RLS lui cache celle de B.
        await ProvisionAsync(EcoleB);

        await using var context = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(context, EcoleA)
            .Handle(new SelectProfileCommand(ProfileType.EnseignementGeneral, StudentQuotaTier.Tier1_150), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();

        await using var owner = _db.NewOwnerContext();
        (await owner.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.SchoolId == EcoleB))
            .Status.Should().Be(TenantSubscriptionStatus.PendingOnboarding);
    }

    private async Task ProvisionAsync(Guid schoolId)
    {
        await using var context = _db.NewAppContext(null);
        (await _db.NewProvisioningStore(context).CreateInitialTenantSubscriptionAsync(schoolId, CancellationToken.None))
            .Should().NotBeNull();
    }

    private static SelectProfileCommandHandler NewHandler(SamaEcole.Persistence.ApplicationDbContext context, Guid schoolId) =>
        new(context, new FixedTenant(schoolId), NullLogger<SelectProfileCommandHandler>.Instance);

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
