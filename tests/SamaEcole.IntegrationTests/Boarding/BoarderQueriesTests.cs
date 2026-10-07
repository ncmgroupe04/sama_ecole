using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Boarders.GetBoarder;
using SamaEcole.Application.Boarding.Boarders.ListBoarders;
using SamaEcole.Application.Boarding.Boarders.UpdateBoarderProfile;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Boarding;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Liste, fiche et profil des pensionnaires (lot C, tâche 4) — dont le masquage de la fiche médicale par rôle.</summary>
[Trait("Category", "MultiTenant")]
public class BoarderQueriesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("ea111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("ea222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeActive = Guid.Parse("ea111111-0000-0000-0000-000000000001");
    private static readonly Guid AnneePassee = Guid.Parse("ea111111-0000-0000-0000-000000000002");
    private static readonly Guid AnneeB = Guid.Parse("ea222222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("ea111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("ea222222-0000-0000-0000-0000000000c1");
    private static readonly Guid PavGarcons = Guid.Parse("ea111111-0000-0000-0000-00000000b001");
    private static readonly Guid PavFilles = Guid.Parse("ea111111-0000-0000-0000-00000000b002");
    private static readonly Guid PavB = Guid.Parse("ea222222-0000-0000-0000-00000000b001");
    private static readonly Guid R101 = Guid.Parse("ea111111-0000-0000-0000-00000000a101");
    private static readonly Guid R102 = Guid.Parse("ea111111-0000-0000-0000-00000000a102");
    private static readonly Guid R201 = Guid.Parse("ea111111-0000-0000-0000-00000000a201");
    private static readonly Guid RB = Guid.Parse("ea222222-0000-0000-0000-00000000a101");
    private static readonly Guid UserId = Guid.Parse("ea111111-0000-0000-0000-0000000000d1");

    private readonly Dictionary<string, Guid> _stays = new();

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeActive, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneePassee, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) },
            new SchoolYear { Id = AnneeB, SchoolId = EcoleB, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 60 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "CM2", Level = "Primaire", Capacity = 60 });
        owner.Dormitories.AddRange(
            new Dormitory { Id = PavGarcons, SchoolId = EcoleA, Name = "Pavillon Garçons", Gender = DormitoryGender.Garcons },
            new Dormitory { Id = PavFilles, SchoolId = EcoleA, Name = "Pavillon Filles", Gender = DormitoryGender.Filles },
            new Dormitory { Id = PavB, SchoolId = EcoleB, Name = "Pavillon B", Gender = DormitoryGender.Garcons });
        owner.DormitoryRooms.AddRange(
            new DormitoryRoom { Id = R101, SchoolId = EcoleA, DormitoryId = PavGarcons, Name = "Chambre 101" },
            new DormitoryRoom { Id = R102, SchoolId = EcoleA, DormitoryId = PavGarcons, Name = "Chambre 102" },
            new DormitoryRoom { Id = R201, SchoolId = EcoleA, DormitoryId = PavFilles, Name = "Chambre 201" },
            new DormitoryRoom { Id = RB, SchoolId = EcoleB, DormitoryId = PavB, Name = "Chambre B" });

        // Fiche de g1 : santé, contact d'urgence, une personne habilitée.
        var g1 = AddStay(owner, "g1", "Alpha Diop", EcoleA, AnneeActive, ClasseA, BoardingRegime.Interne, R101, 1);
        g1.MedicalNotes = "Allergie aux arachides";
        g1.EmergencyContactName = "Mme Diop";
        g1.EmergencyContactPhone = "77 123 45 67";
        g1.AllowedExitPersons = [new AllowedExitPerson { Name = "Oumar Diop", Relationship = "Oncle", Phone = "76 123 45 67" }];
        AddStay(owner, "g2", "Bilal Ndiaye", EcoleA, AnneeActive, ClasseA, BoardingRegime.Interne, R101, 2);
        AddStay(owner, "g3", "Cheikh Fall", EcoleA, AnneeActive, ClasseA, BoardingRegime.DemiPensionnaire, null, 0);
        AddStay(owner, "g4", "Demba Sow", EcoleA, AnneeActive, ClasseA, BoardingRegime.Interne, null, 0);
        AddStay(owner, "f1", "Awa Ba", EcoleA, AnneeActive, ClasseA, BoardingRegime.Interne, R201, 1);
        AddStay(owner, "ended", "Ibou Gaye", EcoleA, AnneeActive, ClasseA, BoardingRegime.Interne, null, 0, active: false);
        AddStay(owner, "ancien", "Zeina Ancienne", EcoleA, AnneePassee, ClasseA, BoardingRegime.Interne, null, 0, active: false);
        AddStay(owner, "b1", "Boubacar Autre", EcoleB, AnneeB, ClasseB, BoardingRegime.Interne, RB, 1);
        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private BoardingEnrollment AddStay(
        ApplicationDbContext owner, string key, string name, Guid school, Guid year, Guid classroom,
        BoardingRegime regime, Guid? room, int bedNumber, bool active = true)
    {
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = school, Matricule = $"M-{key}", FullName = name,
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroom
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = school, StudentId = student.Id, SchoolYearId = year, ClassroomId = classroom,
            Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = $"REC-{key}",
            EnrolledAt = DateTimeOffset.UtcNow
        };
        Guid? bedId = null;
        if (room is { } r)
        {
            var bed = new Bed { SchoolId = school, DormitoryRoomId = r, BedNumber = bedNumber };
            owner.Beds.Add(bed);
            bedId = bed.Id;
        }

        var stay = new BoardingEnrollment
        {
            SchoolId = school, StudentId = student.Id, EnrollmentId = enrollment.Id, Regime = regime, BedId = bedId,
            StartDate = new DateOnly(2026, 9, 15), EndDate = active ? null : new DateOnly(2026, 10, 1), IsActive = active
        };
        _stays[key] = stay.Id;
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
        owner.BoardingEnrollments.Add(stay);
        return stay;
    }

    private async Task<PaginatedBoarders> ListAsync(ListBoardersQuery query)
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        return await new ListBoardersQueryHandler(ctx).Handle(query, CancellationToken.None);
    }

    private async Task<BoarderDetailDto> DetailAsync(Guid id, Role role, Guid? school = null)
    {
        await using var ctx = _db.NewAppContext(school ?? EcoleA);
        return await new GetBoarderQueryHandler(ctx, new TestCurrentUser(UserId, role)).Handle(new GetBoarderQuery(id), CancellationToken.None);
    }

    // ------------------------------------------------------------------ Liste

    [Fact]
    public async Task The_Default_List_Shows_The_Active_Stays_Of_The_Active_Year_Of_The_Current_School_Sorted_By_Name()
    {
        var page = await ListAsync(new ListBoardersQuery());

        page.Items.Select(i => i.StudentName).Should().Equal("Alpha Diop", "Awa Ba", "Bilal Ndiaye", "Cheikh Fall", "Demba Sow");
        page.TotalCount.Should().Be(5, "ni le séjour clos, ni l'année passée, ni l'autre école");
        var alpha = page.Items[0];
        (alpha.RoomName, alpha.DormitoryName, alpha.BedNumber, alpha.ClassroomName, alpha.Regime)
            .Should().Be(("Chambre 101", "Pavillon Garçons", 1, "CM2", BoardingRegime.Interne));
        page.Items.Single(i => i.StudentName == "Cheikh Fall").BedId.Should().BeNull();
    }

    [Fact]
    public async Task The_List_Can_Be_Filtered_By_Dormitory_Room_Regime_And_Status()
    {
        (await ListAsync(new ListBoardersQuery { DormitoryId = PavGarcons })).Items.Select(i => i.StudentName)
            .Should().Equal("Alpha Diop", "Bilal Ndiaye");
        (await ListAsync(new ListBoardersQuery { RoomId = R101 })).TotalCount.Should().Be(2);
        (await ListAsync(new ListBoardersQuery { RoomId = R102 })).TotalCount.Should().Be(0);
        (await ListAsync(new ListBoardersQuery { Regime = BoardingRegime.DemiPensionnaire })).Items.Single().StudentName.Should().Be("Cheikh Fall");
        (await ListAsync(new ListBoardersQuery { Status = "ended" })).Items.Single().StudentName.Should().Be("Ibou Gaye");
        (await ListAsync(new ListBoardersQuery { Status = "awaitingBed" })).Items.Single().StudentName.Should().Be("Demba Sow");
    }

    [Fact]
    public async Task The_List_Can_Be_Searched_By_Name_Or_Matricule_And_Ignores_A_One_Letter_Term()
    {
        (await ListAsync(new ListBoardersQuery { Search = "alp" })).Items.Single().StudentName.Should().Be("Alpha Diop");
        (await ListAsync(new ListBoardersQuery { Search = "m-g2" })).Items.Single().StudentName.Should().Be("Bilal Ndiaye");
        (await ListAsync(new ListBoardersQuery { Search = "a" })).TotalCount.Should().Be(5, "un seul caractère : filtre ignoré");
    }

    [Fact]
    public async Task The_List_Is_Paginated_And_The_Page_Size_Is_Capped_At_100()
    {
        var second = await ListAsync(new ListBoardersQuery { Page = 2, PageSize = 2 });
        second.Items.Select(i => i.StudentName).Should().Equal("Bilal Ndiaye", "Cheikh Fall");
        (second.TotalCount, second.Page, second.PageSize).Should().Be((5, 2, 2));

        var capped = await ListAsync(new ListBoardersQuery { Page = 0, PageSize = 1000 });
        (capped.Page, capped.PageSize).Should().Be((1, 100));
    }

    // ------------------------------------------------------------------ Fiche et masquage

    [Theory]
    [InlineData(Role.Directeur, true)]
    [InlineData(Role.Surveillant, true)]
    [InlineData(Role.Secretariat, false)]
    public async Task The_Medical_Notes_Are_Only_Visible_To_The_Director_And_The_Surveillant(Role role, bool canSee)
    {
        var detail = await DetailAsync(_stays["g1"], role);

        detail.MedicalNotes.Should().Be(canSee ? "Allergie aux arachides" : null);
        detail.EmergencyContactName.Should().Be("Mme Diop", "le reste de la fiche reste visible");
        detail.AllowedExitPersons.Should().ContainSingle().Which.Name.Should().Be("Oumar Diop");
        detail.Boarder.StudentName.Should().Be("Alpha Diop");
    }

    [Fact]
    public async Task A_Boarder_Of_Another_School_Is_Not_Found()
    {
        var act = async () => await DetailAsync(_stays["b1"], Role.Directeur);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ Profil

    private async Task<BoarderDetailDto> UpdateAsync(Guid id, Role role, string? notes, string? contact = "Mme Sow", uint? rowVersion = null,
        IReadOnlyList<AllowedExitPersonDto>? persons = null)
    {
        var token = rowVersion ?? (await DetailAsync(id, Role.Directeur)).Boarder.RowVersion;
        await using var ctx = _db.NewAppContext(EcoleA);
        return await new UpdateBoarderProfileCommandHandler(ctx, new TestCurrentUser(UserId, role)).Handle(
            new UpdateBoarderProfileCommand(id, notes, contact, "77 000 00 00", persons ?? [], token), CancellationToken.None);
    }

    [Theory]
    [InlineData(Role.Directeur)]
    [InlineData(Role.Surveillant)]
    public async Task The_Director_And_The_Surveillant_Can_Write_The_Whole_Profile(Role role)
    {
        var persons = new List<AllowedExitPersonDto>
        {
            new("Oumar Diop", "Oncle", "76 123 45 67"), new("Fatou Diop", "Tante", "78 123 45 67")
        };

        var result = await UpdateAsync(_stays["g1"], role, "Asthme léger", persons: persons);

        result.MedicalNotes.Should().Be("Asthme léger");
        result.EmergencyContactName.Should().Be("Mme Sow");
        result.AllowedExitPersons.Select(p => p.Name).Should().Equal("Oumar Diop", "Fatou Diop");
        (await DetailAsync(_stays["g1"], Role.Directeur)).MedicalNotes.Should().Be("Asthme léger");
    }

    [Fact]
    public async Task The_Secretary_Cannot_Write_Medical_Notes_And_The_Message_Never_Echoes_Them()
    {
        const string secret = "Diagnostic confidentiel XYZ";

        var act = async () => await UpdateAsync(_stays["g1"], Role.Secretariat, secret);

        var thrown = await act.Should().ThrowAsync<ForbiddenException>();
        thrown.Which.Message.Should().NotContain(secret);
        (await DetailAsync(_stays["g1"], Role.Directeur)).MedicalNotes.Should().Be("Allergie aux arachides", "rien n'a été écrit");
    }

    [Fact]
    public async Task The_Secretary_Updates_The_Rest_Of_The_Profile_And_Leaves_The_Medical_File_Untouched()
    {
        var result = await UpdateAsync(_stays["g1"], Role.Secretariat, notes: null, contact: "Mme Ba");

        result.MedicalNotes.Should().BeNull("masquée pour ce rôle");
        result.EmergencyContactName.Should().Be("Mme Ba");
        (await DetailAsync(_stays["g1"], Role.Surveillant)).MedicalNotes.Should().Be("Allergie aux arachides", "conservée");
    }

    [Fact]
    public async Task A_Stale_Token_Or_A_Boarder_Of_Another_School_Is_Rejected()
    {
        var stale = async () => await UpdateAsync(_stays["g1"], Role.Directeur, "x", rowVersion: 1);
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var ctx = _db.NewAppContext(EcoleA);
        var other = async () => await new UpdateBoarderProfileCommandHandler(ctx, new TestCurrentUser(UserId, Role.Directeur))
            .Handle(new UpdateBoarderProfileCommand(_stays["b1"], "x", null, null, [], 0), CancellationToken.None);
        await other.Should().ThrowAsync<KeyNotFoundException>();
    }
}
