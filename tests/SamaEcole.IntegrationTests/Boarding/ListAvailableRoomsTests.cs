using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Rooms.ListAvailableRooms;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Chambres ayant au moins un lit libre (lot F0) — handler + PostgreSQL réel. Alimente le sélecteur de chambre du formulaire d'inscription, qui lisait
/// jusqu'ici l'ancien <c>GET /internat/dashboard</c>. Un lit est « libre » s'il est <c>Available</c> ET qu'aucun séjour ACTIF ne le tient (l'état
/// « occupé » n'est jamais stocké) ; les éléments supprimés et les autres écoles n'apparaissent jamais.
/// </summary>
[Trait("Category", "MultiTenant")]
public class ListAvailableRoomsTests : LeaveTestBase
{
    private async Task<IReadOnlyList<AvailableRoomDto>> ListAsync(Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new ListAvailableRoomsQueryHandler(ctx).Handle(new ListAvailableRoomsQuery(), CancellationToken.None);
    }

    /// <summary>Ajoute une chambre (et ses lits, dans l'état donné) à un pavillon ; renvoie ses identifiants.</summary>
    private async Task<(Guid Room, List<Guid> Beds)> AddRoomAsync(
        Guid school, Guid dormitory, string name, params BedStatus[] beds)
    {
        await using var owner = Db.NewOwnerContext();
        var room = new DormitoryRoom { Id = Guid.NewGuid(), SchoolId = school, DormitoryId = dormitory, Name = name };
        owner.DormitoryRooms.Add(room);
        var ids = new List<Guid>();
        for (var i = 0; i < beds.Length; i++)
        {
            var bed = new Bed { Id = Guid.NewGuid(), SchoolId = school, DormitoryRoomId = room.Id, BedNumber = i + 1, Status = beds[i] };
            owner.Beds.Add(bed);
            ids.Add(bed.Id);
        }

        await owner.SaveChangesAsync();
        return (room.Id, ids);
    }

    [Fact]
    public async Task A_Room_With_Free_Beds_Is_Listed_With_Its_Dormitory_Gender_And_Free_Count()
    {
        var (room, _) = await AddRoomAsync(EcoleA, PavA, "Chambre 103", BedStatus.Available, BedStatus.Available, BedStatus.Available);

        var item = (await ListAsync()).Should().ContainSingle(r => r.RoomId == room).Subject;

        (item.RoomName, item.DormitoryId, item.DormitoryName, item.Gender, item.FreeBeds)
            .Should().Be(("Chambre 103", PavA, "Pavillon Oustaz Ahmad", DormitoryGender.Garcons, 3));
    }

    [Fact]
    public async Task A_Bed_Held_By_An_Active_Stay_Is_Not_Free()
    {
        var (room, beds) = await AddRoomAsync(EcoleA, PavA, "Chambre 104", BedStatus.Available, BedStatus.Available);
        await using (var owner = Db.NewOwnerContext())
        {
            // « attente » est un interne actif sans lit : on lui donne le premier lit de la chambre.
            var stay = await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == Stays["attente"]);
            stay.BedId = beds[0];
            await owner.SaveChangesAsync();
        }

        (await ListAsync()).Should().ContainSingle(r => r.RoomId == room).Which.FreeBeds.Should().Be(1);
    }

    [Fact]
    public async Task A_Bed_In_Maintenance_Is_Not_Free()
    {
        var (room, _) = await AddRoomAsync(EcoleA, PavA, "Chambre 105", BedStatus.Maintenance, BedStatus.Available);

        (await ListAsync()).Should().ContainSingle(r => r.RoomId == room).Which.FreeBeds.Should().Be(1);
    }

    [Fact]
    public async Task A_Full_Room_And_A_Room_Whose_Beds_Are_All_In_Maintenance_Are_Not_Listed()
    {
        var (maintenance, _) = await AddRoomAsync(EcoleA, PavA, "Chambre 106", BedStatus.Maintenance, BedStatus.Maintenance);

        var rooms = await ListAsync();

        rooms.Should().NotContain(r => r.RoomName == "Chambre 101", "ses deux lits sont tenus par m1 et m2");
        rooms.Should().NotContain(r => r.RoomId == maintenance);
    }

    [Fact]
    public async Task A_Room_Without_Any_Bed_Is_Not_Listed()
    {
        var (room, _) = await AddRoomAsync(EcoleA, PavA, "Chambre vide");

        (await ListAsync()).Should().NotContain(r => r.RoomId == room);
    }

    [Fact]
    public async Task A_Deleted_Room_Bed_Or_Dormitory_Never_Appears()
    {
        var (deletedRoom, _) = await AddRoomAsync(EcoleA, PavA, "Chambre supprimée", BedStatus.Available);
        var (roomWithDeletedBed, beds) = await AddRoomAsync(EcoleA, PavA, "Chambre lit supprimé", BedStatus.Available, BedStatus.Available);
        var (roomOfDeletedDormitory, _) = await AddRoomAsync(EcoleA, PavA2, "Chambre pavillon supprimé", BedStatus.Available);

        await using (var owner = Db.NewOwnerContext())
        {
            (await owner.DormitoryRooms.IgnoreQueryFilters().SingleAsync(r => r.Id == deletedRoom)).SoftDelete("test");
            (await owner.Beds.IgnoreQueryFilters().SingleAsync(b => b.Id == beds[0])).SoftDelete("test");
            (await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == PavA2)).SoftDelete("test");
            await owner.SaveChangesAsync();
        }

        var rooms = await ListAsync();

        rooms.Should().NotContain(r => r.RoomId == deletedRoom);
        rooms.Should().NotContain(r => r.RoomId == roomOfDeletedDormitory, "un pavillon supprimé masque ses chambres");
        rooms.Should().ContainSingle(r => r.RoomId == roomWithDeletedBed).Which.FreeBeds.Should().Be(1, "le lit supprimé ne compte pas");
    }

    [Fact]
    public async Task Another_Schools_Rooms_Are_Never_Returned_And_Each_School_Sees_Its_Own()
    {
        var (roomB, _) = await AddRoomAsync(EcoleB, PavEcoleB, "Chambre B2", BedStatus.Available);
        var (roomA, _) = await AddRoomAsync(EcoleA, PavA, "Chambre A9", BedStatus.Available);

        var forA = await ListAsync(EcoleA);
        var forB = await ListAsync(EcoleB);

        forA.Should().Contain(r => r.RoomId == roomA).And.NotContain(r => r.RoomId == roomB);
        forB.Should().Contain(r => r.RoomId == roomB).And.NotContain(r => r.RoomId == roomA);
    }

    [Fact]
    public async Task Rooms_Are_Sorted_By_Dormitory_Then_Room_Name()
    {
        await AddRoomAsync(EcoleA, PavA2, "Chambre 202", BedStatus.Available);
        await AddRoomAsync(EcoleA, PavA, "Chambre 108", BedStatus.Available);
        await AddRoomAsync(EcoleA, PavA, "Chambre 107", BedStatus.Available);

        var labels = (await ListAsync()).Select(r => $"{r.DormitoryName}|{r.RoomName}").ToList();

        labels.Should().ContainInOrder("Pavillon Oustaz Ahmad|Chambre 107", "Pavillon Oustaz Ahmad|Chambre 108", "Pavillon Oustaz Bamba|Chambre 202");
    }
}
