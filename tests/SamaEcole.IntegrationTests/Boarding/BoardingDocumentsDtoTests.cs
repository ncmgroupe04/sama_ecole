using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Attendance;
using SamaEcole.Application.Boarding.Attendance.RecordAttendance;
using SamaEcole.Application.Boarding.Documents.BoarderSheet;
using SamaEcole.Application.Boarding.Documents.DormitoryRegister;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// DTO d'impression de la fiche pensionnaire et du registre du pavillon (lot E, tâche 2) — déjà MASQUÉS par rôle avant
/// d'arriver au moteur PDF. Aujourd'hui = 12/10/2026.
/// </summary>
[Trait("Category", "MultiTenant")]
public class BoardingDocumentsDtoTests : LeaveTestBase
{
    private const string MedicalSecret = "Allergie sévère aux arachides (EpiPen)";

    // Sans cet encodeur, « é » serait écrit é et toute recherche de la valeur échouerait : un test « absente » passerait à vide.
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private async Task SeedDetailsAsync()
    {
        await using var owner = Db.NewOwnerContext();
        var m1 = await owner.BoardingEnrollments.IgnoreQueryFilters().SingleAsync(b => b.Id == Stays["m1"]);
        m1.MedicalNotes = MedicalSecret;
        m1.EmergencyContactName = "Mme Diop";
        m1.EmergencyContactPhone = "77 123 45 67";

        var student = await owner.Students.IgnoreQueryFilters().SingleAsync(s => s.Id == m1.StudentId);
        student.FullNameAr = "محمد ديوب";

        var pavillon = await owner.Dormitories.IgnoreQueryFilters().SingleAsync(d => d.Id == PavA);
        pavillon.SupervisorName = "M. Ndiaye";
        pavillon.SupervisorPhone = "77 000 11 22";
        await owner.SaveChangesAsync();
    }

    private async Task<BoarderSheetDto> SheetAsync(Guid boarder, Role role, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new GetBoarderSheetQueryHandler(ctx, new TestCurrentUser(Guid.NewGuid(), role), Clock)
            .Handle(new GetBoarderSheetQuery(boarder), CancellationToken.None);
    }

    private async Task<DormitoryRegisterDto> RegisterAsync(Guid dormitory, DateOnly? date = null, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new GetDormitoryRegisterQueryHandler(ctx, Clock)
            .Handle(new GetDormitoryRegisterQuery(dormitory, date), CancellationToken.None);
    }

    private async Task AddLeaveAsync(string key, DateOnly leave, DateOnly expected, DateOnly? returned = null)
    {
        await using var owner = Db.NewOwnerContext();
        owner.BoardingLeaves.Add(new BoardingLeave
        {
            SchoolId = EcoleA, BoardingEnrollmentId = Stays[key], LeaveDate = leave, ExpectedReturnDate = expected,
            ActualReturnDate = returned, Reason = BoardingLeaveReason.Famille, AccompaniedBy = "Parent"
        });
        await owner.SaveChangesAsync();
    }

    [Theory]
    [InlineData(Role.Directeur, true)]
    [InlineData(Role.Surveillant, true)]
    [InlineData(Role.Secretariat, false)]
    public async Task The_Medical_Notes_Reach_The_Sheet_Only_For_Director_And_Supervisor(Role role, bool sees)
    {
        await SeedDetailsAsync();

        var sheet = await SheetAsync(Stays["m1"], role);

        sheet.MedicalNotes.Should().Be(sees ? MedicalSecret : null);
        JsonSerializer.Serialize(sheet, Readable).Contains(MedicalSecret).Should().Be(sees, "la valeur ne doit nulle part ailleurs dans le DTO");
    }

    [Fact]
    public async Task Everything_Except_The_Medical_Notes_Is_Identical_Across_Roles()
    {
        await SeedDetailsAsync();

        var director = await SheetAsync(Stays["m1"], Role.Directeur);
        var secretary = await SheetAsync(Stays["m1"], Role.Secretariat);

        director.Should().BeEquivalentTo(secretary, o => o.Excluding(s => s.MedicalNotes).Excluding(s => s.GeneratedAt));
    }

    [Fact]
    public async Task The_Sheet_Carries_The_Identity_The_School_And_The_Emergency_Contact()
    {
        await SeedDetailsAsync();

        var sheet = await SheetAsync(Stays["m1"], Role.Secretariat);

        (sheet.SchoolName, sheet.SchoolYearLabel).Should().Be(("École A", "2026-2027"));
        (sheet.Boarder.StudentName, sheet.Boarder.Matricule, sheet.Boarder.RoomName).Should().Be(("Élève m1", "M-m1", "Chambre 101"));
        (sheet.FullNameAr, sheet.BirthDate, sheet.BirthPlace).Should().Be(("محمد ديوب", new DateOnly(2014, 1, 1), "Dakar"));
        (sheet.EmergencyContactName, sheet.EmergencyContactPhone).Should().Be(("Mme Diop", "77 123 45 67"));
        sheet.AllowedExitPersons.Should().ContainSingle().Which.Name.Should().Be("Fatou Diop");
        sheet.GeneratedAt.Should().Be(Clock.GetUtcNow());
    }

    [Fact]
    public async Task The_Sheet_Lists_All_The_Leaves_Of_The_Active_Year_Newest_First_And_Not_Older_Ones()
    {
        await AddLeaveAsync("m1", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 22), returned: new DateOnly(2026, 9, 22));
        await AddLeaveAsync("m1", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 14));
        await AddLeaveAsync("m1", new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 3), returned: new DateOnly(2025, 10, 3));   // année passée

        var sheet = await SheetAsync(Stays["m1"], Role.Directeur);

        sheet.Leaves.Select(l => l.LeaveDate).Should().Equal(new DateOnly(2026, 10, 10), new DateOnly(2026, 9, 20));
    }

    [Fact]
    public async Task An_Ended_Stay_Is_Still_Printable_As_An_Archive()
    {
        var sheet = await SheetAsync(Stays["clos"], Role.Directeur);

        sheet.Boarder.IsActive.Should().BeFalse();
        sheet.Boarder.EndDate.Should().NotBeNull();
    }

    [Fact]
    public async Task A_Foreign_Or_Unknown_Stay_Is_Not_Found()
    {
        var foreign = async () => await SheetAsync(Stays["autre"], Role.Directeur);          // tenant École A, séjour de l'École B
        var unknown = async () => await SheetAsync(Guid.NewGuid(), Role.Directeur);

        await foreign.Should().ThrowAsync<KeyNotFoundException>();
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ------------------------------------------------------------------ Registre

    [Fact]
    public async Task The_Register_Lists_The_Dormitory_Boarders_By_Room_And_Bed_Without_Phone_Or_Health_Data()
    {
        await SeedDetailsAsync();

        var register = await RegisterAsync(PavA);

        (register.SchoolName, register.DormitoryName, register.SupervisorName, register.Date)
            .Should().Be(("École A", "Pavillon Oustaz Ahmad", "M. Ndiaye", Today), "date du jour par défaut");
        register.Rows.Select(r => (r.RoomName, r.BedNumber, r.Matricule))
            .Should().Equal(("Chambre 101", 1, "M-m1"), ("Chambre 101", 2, "M-m2"), ("Chambre 102", 1, "M-m3"));
        register.Rows[0].Should().Match<DormitoryRegisterRowDto>(r => r.StudentNameAr == "محمد ديوب" && r.ClassroomName == "CM2");

        var json = JsonSerializer.Serialize(register, Readable);
        json.Should().Contain("Élève m1", "jumeau : la recherche voit bien les accents, l'absence ci-dessous n'est donc pas vide de sens");
        json.Should().NotContain(MedicalSecret).And.NotContain("77 000 11 22").And.NotContain("77 123 45 67");
    }

    [Fact]
    public async Task The_Register_Flags_Leave_Absentees_And_Reuses_Recorded_Attendance()
    {
        await AddLeaveAsync("m1", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 14));
        await using (var ctx = Db.NewAppContext(EcoleA))
        {
            await new RecordAttendanceCommandHandler(ctx, Clock).Handle(
                new RecordAttendanceCommand(Today, PavA, [new AttendanceEntryInput(Stays["m2"], true, "RAS")]), CancellationToken.None);
        }

        var register = await RegisterAsync(PavA, Today);

        var m1 = register.Rows.Single(r => r.Matricule == "M-m1");
        (m1.OnLeave, m1.Recorded, m1.IsPresent, m1.Note).Should().Be((true, false, false, "En permission"));
        var m2 = register.Rows.Single(r => r.Matricule == "M-m2");
        (m2.Recorded, m2.IsPresent, m2.Note).Should().Be((true, true, "RAS"));
        var m3 = register.Rows.Single(r => r.Matricule == "M-m3");
        (m3.Recorded, m3.IsPresent).Should().Be((false, (bool?)null));
    }

    [Fact]
    public async Task The_Register_Excludes_Half_Boarders_And_Boarders_Without_A_Bed()
    {
        var register = await RegisterAsync(PavA);

        register.Rows.Select(r => r.Matricule).Should().NotContain(["M-demi", "M-attente", "M-clos", "M-m4"]);
    }

    [Fact]
    public async Task The_Register_Of_A_Future_Date_Or_A_Foreign_Dormitory_Is_Refused()
    {
        var future = async () => await RegisterAsync(PavA, new DateOnly(2026, 10, 13));
        var foreign = async () => await RegisterAsync(PavEcoleB);

        await future.Should().ThrowAsync<ValidationException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>();
    }
}
