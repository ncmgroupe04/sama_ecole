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
                db, new StubTenantProvider(Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
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
                db, new StubTenantProvider(Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
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
                    db, new StubTenantProvider(Ecole), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
                .Handle(new ApplyEstablishmentProfileCommand("FrancoArabe"), CancellationToken.None);
        }

        await using var readDb = _db.NewAppContext(Ecole);
        var settings = readDb.SchoolSettings.Single();

        settings.ProfileEtablissement.Should().Be(ProfileEtablissement.FrancoArabe);
        settings.IsCoranModuleEnabled.Should().BeTrue();
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
                db, new StubTenantProvider(schoolSansReglages), NullLogger<ApplyEstablishmentProfileCommandHandler>.Instance)
            .Handle(new ApplyEstablishmentProfileCommand("General"), CancellationToken.None);

        result.ProfileEtablissement.Should().Be("General");
    }
}

file sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
