using FluentAssertions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Subscriptions;

/// <summary>
/// Ticket JGK-B03 — passage automatique en lecture seule à expiration, côté base. Tout s'exécute avec
/// le RÔLE APPLICATIF et SANS tenant : c'est la situation exacte de SubscriptionLifecycleHostedService,
/// qui tourne hors requête HTTP et ne porte donc aucun SchoolId de session — seule la fonction SECURITY
/// DEFINER expire_overdue_subscriptions doit pouvoir traverser la RLS de `subscriptions`.
/// </summary>
public class SubscriptionAdminStoreTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleExpiree = Guid.Parse("11111111-2222-1111-1111-111111111111");
    private static readonly Guid EcoleEncoreValide = Guid.Parse("22222222-2222-1111-1111-111111111112");
    private static readonly Guid EcoleDejaReadOnly = Guid.Parse("33333333-2222-1111-1111-111111111113");
    private static readonly Guid EcoleSansEcheance = Guid.Parse("44444444-2222-1111-1111-111111111114");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleExpiree, Name = "École Expirée" },
            new School { Id = EcoleEncoreValide, Name = "École Encore Valide" },
            new School { Id = EcoleDejaReadOnly, Name = "École Déjà En Lecture Seule" },
            new School { Id = EcoleSansEcheance, Name = "École Sans Échéance" });

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var nextMonth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));

        owner.Subscriptions.AddRange(
            new Subscription
            {
                SchoolId = EcoleExpiree, Plan = SubscriptionPlan.Standard,
                Status = SubscriptionStatus.Active, ExpiresAt = yesterday
            },
            new Subscription
            {
                SchoolId = EcoleEncoreValide, Plan = SubscriptionPlan.Standard,
                Status = SubscriptionStatus.Active, ExpiresAt = nextMonth
            },
            new Subscription
            {
                // Déjà ReadOnly : ne doit pas réapparaître dans le résultat (rien à re-notifier).
                SchoolId = EcoleDejaReadOnly, Plan = SubscriptionPlan.Standard,
                Status = SubscriptionStatus.ReadOnly, ExpiresAt = yesterday
            },
            new Subscription
            {
                // AwaitingPayment sans échéance (parcours self-service) : ExpiresAt null ne doit
                // jamais faire planter la comparaison de dates ni basculer par erreur.
                SchoolId = EcoleSansEcheance, Plan = SubscriptionPlan.Standard,
                Status = SubscriptionStatus.AwaitingPayment, ExpiresAt = null
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Should_Switch_Only_Active_Subscriptions_Past_Their_Expiry_Date_To_ReadOnly()
    {
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewSubscriptionAdminStore(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expiredSchoolIds = await store.ExpireOverdueSubscriptionsAsync(today, CancellationToken.None);

        expiredSchoolIds.Should().ContainSingle().Which.Should().Be(EcoleExpiree);

        // IgnoreQueryFilters : Subscription implémente ITenantEntity et NewOwnerContext() utilise
        // StubTenantProvider(null) — sans ceci, le Global Query Filter deviendrait "SchoolId == null"
        // et ne trouverait jamais aucune ligne réelle (même piège que AuthApiFactory.GetSubscriptionAsync).
        await using var owner = _db.NewOwnerContext();
        var refreshed = await owner.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.SchoolId == EcoleExpiree)
            .Select(s => s.Status)
            .SingleAsync();
        refreshed.Should().Be(SubscriptionStatus.ReadOnly);
    }

    [Fact]
    public async Task Should_Not_Touch_A_Subscription_That_Is_Not_Yet_Due()
    {
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewSubscriptionAdminStore(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expiredSchoolIds = await store.ExpireOverdueSubscriptionsAsync(today, CancellationToken.None);

        expiredSchoolIds.Should().NotContain(EcoleEncoreValide);

        await using var owner = _db.NewOwnerContext();
        var status = await owner.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.SchoolId == EcoleEncoreValide)
            .Select(s => s.Status)
            .SingleAsync();
        status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Should_Be_Idempotent_When_Run_Twice_In_A_Row()
    {
        await using var db = _db.NewAppContext(schoolId: null);
        var store = _db.NewSubscriptionAdminStore(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await store.ExpireOverdueSubscriptionsAsync(today, CancellationToken.None);
        var secondRun = await store.ExpireOverdueSubscriptionsAsync(today, CancellationToken.None);

        secondRun.Should().BeEmpty(
            "un abonnement déjà basculé en ReadOnly ne doit pas redéclencher de notification à chaque tour");
    }
}
