using FluentAssertions;
using SamaEcole.Application.Buildings.Queries.GetBuildingsWithRooms;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Décision Q1 (spec 2026-10-06) : les anciennes salles de type <c>Dortoir</c> disparaissent de la gestion classique
/// des bâtiments et salles, pour ne pas faire doublon avec les pavillons du module Internat.
/// </summary>
[Trait("Category", "MultiTenant")]
public class LegacyDormitoryRoomsHiddenTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("f6111111-1111-1111-1111-111111111111");
    private static readonly Guid BlocClasses = Guid.Parse("f6111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavillonRepris = Guid.Parse("f6111111-0000-0000-0000-00000000b002");
    private static readonly Guid BatimentMixte = Guid.Parse("f6111111-0000-0000-0000-00000000b003");
    private static readonly Guid BatimentVide = Guid.Parse("f6111111-0000-0000-0000-00000000b004");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });
        owner.Buildings.AddRange(
            new Building { Id = BlocClasses, SchoolId = EcoleA, Name = "Bloc classes" },
            new Building { Id = PavillonRepris, SchoolId = EcoleA, Name = "Pavillon repris" },
            new Building { Id = BatimentMixte, SchoolId = EcoleA, Name = "Bâtiment mixte" },
            new Building { Id = BatimentVide, SchoolId = EcoleA, Name = "Bâtiment neuf" });
        owner.Rooms.AddRange(
            new Room { SchoolId = EcoleA, BuildingId = BlocClasses, Name = "Salle 1", Capacity = 30, Type = RoomType.SalleDeClasse },
            new Room { SchoolId = EcoleA, BuildingId = PavillonRepris, Name = "Dortoir 101", Capacity = 6, Type = RoomType.Dortoir },
            new Room { SchoolId = EcoleA, BuildingId = BatimentMixte, Name = "Salle 2", Capacity = 30, Type = RoomType.SalleDeClasse },
            new Room { SchoolId = EcoleA, BuildingId = BatimentMixte, Name = "Dortoir 201", Capacity = 8, Type = RoomType.Dortoir });
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task The_Classic_Rooms_Screen_Hides_Dortoir_Rooms_And_Buildings_That_Only_Hold_Dortoirs()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var buildings = await new GetBuildingsWithRoomsQueryHandler(ctx)
            .Handle(new GetBuildingsWithRoomsQuery(), CancellationToken.None);

        buildings.Select(b => b.Name).Should().BeEquivalentTo("Bloc classes", "Bâtiment mixte", "Bâtiment neuf");
        buildings.Single(b => b.Id == BatimentMixte).Rooms.Select(r => r.Name).Should().Equal("Salle 2");
        buildings.SelectMany(b => b.Rooms).Should().NotContain(r => r.Type == "Dortoir");
    }

    [Fact]
    public async Task A_Building_Without_Any_Room_Stays_Listed()
    {
        await using var ctx = _db.NewAppContext(EcoleA);

        var buildings = await new GetBuildingsWithRoomsQueryHandler(ctx)
            .Handle(new GetBuildingsWithRoomsQuery(), CancellationToken.None);

        buildings.Single(b => b.Id == BatimentVide).Rooms.Should().BeEmpty();
    }
}
