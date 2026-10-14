using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SamaEcole.IntegrationTests.Schools;

/// <summary>
/// Onboarding (Setup Wizard) — ApplyEstablishmentProfileCommand doit appliquer le PRESET de modules en
/// même temps que le profil, sous la RLS réelle (rôle applicatif), pas seulement en mémoire.
/// </summary>
public class ApplyEstablishmentProfileTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();
    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = Ecole, Name = "École Test Onboarding" });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task DaaraInternat_Profile_Enables_Internat_And_Coran()
    {
        await using var db = _db.NewAppContext(Ecole);

        var result = await new ApplyEstablishmentProfileCommandHandler(
                db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
            .Handle(new ApplyEstablishmentProfileCommand("DaaraInternat"), CancellationToken.None);

        result.ProfileEtablissement.Should().Be("DaaraInternat");
        result.IsInternatEnabled.Should().BeTrue();
        result.IsCoranModuleEnabled.Should().BeTrue();
        result.IsPedagogyEnabled.Should().BeTrue();
        result.IsFinanceEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Simplifie_Profile_Disables_Pedagogy()
    {
        await using var db = _db.NewAppContext(Ecole);

        var result = await new ApplyEstablishmentProfileCommandHandler(
                db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
            .Handle(new ApplyEstablishmentProfileCommand("Simplifie"), CancellationToken.None);

        result.ProfileEtablissement.Should().Be("Simplifie");
        result.IsPedagogyEnabled.Should().BeFalse();
        result.IsFinanceEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Applying_A_Profile_Persists_It()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new ApplyEstablishmentProfileCommandHandler(
                    db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
                .Handle(new ApplyEstablishmentProfileCommand("FrancoArabe"), CancellationToken.None);
        }

        await using var readDb = _db.NewAppContext(Ecole);
        var settings = readDb.SchoolSettings.Single();

        settings.ProfileEtablissement.Should().Be(ProfileEtablissement.FrancoArabe);
        settings.IsCoranModuleEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Applying_The_Daara_Memorisation_Profile_Turns_Pedagogy_Off_And_Keeps_Boarding_Quran_And_Finance()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new ApplyEstablishmentProfileCommandHandler(
                    db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
                .Handle(new ApplyEstablishmentProfileCommand("DaaraMemorisation"), CancellationToken.None);
        }

        await using var readDb = _db.NewAppContext(Ecole);
        var settings = readDb.SchoolSettings.Single();

        settings.ProfileEtablissement.Should().Be(ProfileEtablissement.DaaraMemorisation);
        settings.IsPedagogyEnabled.Should().BeFalse();
        settings.IsFinanceEnabled.Should().BeTrue();
        settings.IsInternatEnabled.Should().BeTrue();
        settings.IsCoranModuleEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task During_Onboarding_Changing_The_Profile_Keeps_The_Commercial_Subscription_Aligned_And_Leaves_Tier_And_Status_Alone()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            // Pendant l'Onboarding le profil se choisit librement : le verrou d'abonnement ne s'applique pas encore.
            var subscription = RlsTestDatabase.UnlimitedSubscription(Ecole);
            subscription.StudentQuotaTier = StudentQuotaTier.Tier2_400;
            subscription.MaxStudentLimit = 400;
            subscription.SoftQuotaLimit = 420;
            subscription.Status = TenantSubscriptionStatus.PendingOnboarding;
            owner.TenantSubscriptions.Add(subscription);
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = _db.NewAppContext(Ecole))
        {
            await new ApplyEstablishmentProfileCommandHandler(
                    db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
                .Handle(new ApplyEstablishmentProfileCommand("DaaraInternat"), CancellationToken.None);
        }

        await using var check = _db.NewAppContext(Ecole);
        var row = check.TenantSubscriptions.Single();
        row.ProfileType.Should().Be(ProfileType.InternatDaara);
        (row.IsInternatEnabled, row.IsCoranModuleEnabled).Should().Be((true, true));
        (row.StudentQuotaTier, row.MaxStudentLimit, row.SoftQuotaLimit, row.Status)
            .Should().Be((StudentQuotaTier.Tier2_400, 400, 420, TenantSubscriptionStatus.PendingOnboarding));
    }

    [Fact]
    public async Task Once_The_Subscription_Is_Active_Changing_The_Profile_Is_Intercepted_And_Nothing_Is_Written()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.TenantSubscriptions.Add(RlsTestDatabase.UnlimitedSubscription(Ecole));
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = _db.NewAppContext(Ecole))
        {
            var act = async () => await new ApplyEstablishmentProfileCommandHandler(
                    db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
                .Handle(new ApplyEstablishmentProfileCommand("DaaraInternat"), CancellationToken.None);

            var thrown = (await act.Should().ThrowAsync<SamaEcole.Application.Common.Exceptions.SubscriptionChangeRequiredException>()).Which;

            thrown.Preview.HasChanges.Should().BeTrue();
            thrown.Preview.Request.Profile.Should().Be(ProfileType.InternatDaara);
            thrown.Preview.Changes.Select(c => c.Field).Should().Contain("Profile");
        }

        await using var check = _db.NewAppContext(Ecole);
        check.TenantSubscriptions.Single().ProfileType.Should().Be(ProfileType.EnseignementGeneral, "rien n'a été écrit");
        check.SchoolSettings.Should().BeEmpty("l'interception précède toute écriture, y compris la création des réglages");
    }

    [Fact]
    public async Task Once_The_Subscription_Is_Active_Reapplying_The_Current_Profile_Changes_Nothing()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            var subscription = RlsTestDatabase.UnlimitedSubscription(Ecole);
            subscription.IsInternatEnabled = true; // droit souscrit en plus du preset du profil Général
            owner.TenantSubscriptions.Add(subscription);
            owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, ProfileEtablissement = ProfileEtablissement.General, IsInternatEnabled = true });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using (var db = _db.NewAppContext(Ecole))
        {
            var result = await new ApplyEstablishmentProfileCommandHandler(
                    db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
                .Handle(new ApplyEstablishmentProfileCommand("General"), CancellationToken.None);

            result.IsInternatEnabled.Should().BeTrue("réappliquer le preset aurait effacé le module souscrit en plus");
        }

        await using var check = _db.NewAppContext(Ecole);
        check.TenantSubscriptions.Single().IsInternatEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Changing_The_Profile_Never_Overwrites_The_Managed_Cycles_Set_By_The_Director()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, ManagedCycles = "Primaire,College" });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        // Vers l'Élémentaire (qui, à l'Onboarding, limiterait à Maternelle+Primaire) puis vers un autre profil :
        // dans les deux cas le réglage du Directeur reste tel quel.
        foreach (var profile in new[] { "ElementairePrimaire", "DaaraInternat", "General" })
        {
            await using var db = _db.NewAppContext(Ecole);
            var result = await new ApplyEstablishmentProfileCommandHandler(
                    db, new StubTenantProvider(Ecole), _db.NewChangeService(db, Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
                .Handle(new ApplyEstablishmentProfileCommand(profile), CancellationToken.None);

            result.ManagedCycles.Should().Equal("Primaire", "College");
        }

        await using var check = _db.NewAppContext(Ecole);
        check.SchoolSettings.Single().ManagedCycles.Should().Be("Primaire,College");
    }

    [Fact]
    public async Task Creates_Settings_Row_When_None_Exists_Yet()
    {
        // École sans ligne school_settings (chemin défensif — en pratique couvert dès la création par
        // provision_school_director, mais le Handler doit rester robuste comme ses pairs
        // UpdateSchoolSettingsCommandHandler/UpdateGradingScaleCommandHandler).
        await using var owner = _db.NewOwnerContext();
        var schoolSansReglages = Guid.NewGuid();
        owner.Schools.Add(new School { Id = schoolSansReglages, Name = "École Sans Réglages" });
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var db = _db.NewAppContext(schoolSansReglages);

        var result = await new ApplyEstablishmentProfileCommandHandler(
                db, new StubTenantProvider(schoolSansReglages), _db.NewChangeService(db, schoolSansReglages), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
            .Handle(new ApplyEstablishmentProfileCommand("General"), CancellationToken.None);

        result.ProfileEtablissement.Should().Be("General");
    }
}

file sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
