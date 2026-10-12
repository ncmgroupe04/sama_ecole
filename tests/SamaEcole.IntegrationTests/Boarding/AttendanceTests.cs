using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Attendance;
using SamaEcole.Application.Boarding.Attendance.GetAttendanceSheet;
using SamaEcole.Application.Boarding.Attendance.RecordAttendance;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Feuille d'appel et pointage de nuit collectif (lot D, tâche 5) — handlers + PostgreSQL réel. Aujourd'hui = 12/10.</summary>
[Trait("Category", "MultiTenant")]
public class AttendanceTests : LeaveTestBase
{
    private static readonly DateOnly Night = new(2026, 10, 11);

    private AttendanceEntryInput Entry(string key, bool present, string? note = null) => new(Stays[key], present, note);

    private async Task<RecordAttendanceResult> RecordAsync(
        DateOnly date, Guid dormitory, AttendanceEntryInput[] entries, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new RecordAttendanceCommandHandler(ctx, Clock)
            .Handle(new RecordAttendanceCommand(date, dormitory, entries), CancellationToken.None);
    }

    private async Task<AttendanceSheetDto> SheetAsync(DateOnly date, Guid dormitory, Guid? school = null)
    {
        await using var ctx = Db.NewAppContext(school ?? EcoleA);
        return await new GetAttendanceSheetQueryHandler(ctx, Clock)
            .Handle(new GetAttendanceSheetQuery(dormitory, date), CancellationToken.None);
    }

    private async Task AddLeaveAsync(string key, DateOnly leave, DateOnly? returned = null)
    {
        await using var owner = Db.NewOwnerContext();
        owner.BoardingLeaves.Add(new BoardingLeave
        {
            SchoolId = EcoleA, BoardingEnrollmentId = Stays[key], LeaveDate = leave, ExpectedReturnDate = leave.AddDays(3),
            ActualReturnDate = returned, Reason = BoardingLeaveReason.Famille, AccompaniedBy = "Parent"
        });
        await owner.SaveChangesAsync();
    }

    private async Task<List<BoardingAttendance>> RowsAsync(DateOnly date)
    {
        await using var owner = Db.NewOwnerContext();
        return await owner.BoardingAttendances.IgnoreQueryFilters().AsNoTracking().Where(a => a.Date == date).ToListAsync();
    }

    [Fact]
    public async Task Recording_Creates_One_Row_Per_Boarder_And_Upserts_On_Replay()
    {
        var first = await RecordAsync(Night, PavA, [Entry("m1", true), Entry("m2", false, "Malade")]);
        first.Should().Match<RecordAttendanceResult>(r => r.Saved == 2 && r.Rejected == 0);
        (await RowsAsync(Night)).Should().HaveCount(2);

        await RecordAsync(Night, PavA, [Entry("m1", false, "Parti tard")]);

        var rows = await RowsAsync(Night);
        rows.Should().HaveCount(2, "un pointage par pensionnaire et par nuit : la saisie corrigée met à jour la ligne");
        rows.Single(r => r.BoardingEnrollmentId == Stays["m1"]).Should().Match<BoardingAttendance>(r => !r.IsPresent && r.Note == "Parti tard");
        rows.Single(r => r.BoardingEnrollmentId == Stays["m2"]).Note.Should().Be("Malade");
    }

    [Fact]
    public async Task A_Future_Date_A_Date_Outside_The_Active_Year_Or_A_Foreign_Dormitory_Is_Refused()
    {
        var future = async () => await RecordAsync(new DateOnly(2026, 10, 13), PavA, [Entry("m1", true)]);
        var beforeYear = async () => await RecordAsync(new DateOnly(2026, 8, 31), PavA, [Entry("m1", true)]);
        var unknown = async () => await RecordAsync(Night, Guid.NewGuid(), [Entry("m1", true)]);
        var foreign = async () => await RecordAsync(Night, PavEcoleB, [Entry("m1", true)]);

        await future.Should().ThrowAsync<ValidationException>();
        await beforeYear.Should().ThrowAsync<ValidationException>();
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
        await foreign.Should().ThrowAsync<KeyNotFoundException>("le pavillon de l'école B n'existe pas pour l'école A");
        (await RowsAsync(Night)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_Boarder_On_Leave_Is_Accepted_Only_As_Absent_With_A_Default_Note()
    {
        await AddLeaveAsync("m1", new DateOnly(2026, 10, 10));

        var present = await RecordAsync(Night, PavA, [Entry("m1", true)]);
        present.Lines.Single().Should().Match<AttendanceLineResult>(l => l.Outcome == "Rejected" && l.Code == "BOARDER_ON_LEAVE");
        (await RowsAsync(Night)).Should().BeEmpty();

        var absent = await RecordAsync(Night, PavA, [Entry("m1", false)]);
        absent.Saved.Should().Be(1);
        (await RowsAsync(Night)).Single().Note.Should().Be("En permission");

        await RecordAsync(Night, PavA, [Entry("m1", false, "Chez sa mère")]);
        (await RowsAsync(Night)).Single().Note.Should().Be("Chez sa mère", "une note saisie remplace la note par défaut");
    }

    [Fact]
    public async Task The_Night_Of_The_Return_Counts_As_In_The_Dormitory()
    {
        await AddLeaveAsync("m1", new DateOnly(2026, 10, 10), returned: Night);

        var returnNight = await RecordAsync(Night, PavA, [Entry("m1", true)]);
        var awayNight = await RecordAsync(new DateOnly(2026, 10, 10), PavA, [Entry("m1", true)]);

        returnNight.Saved.Should().Be(1);
        awayNight.Lines.Single().Code.Should().Be("BOARDER_ON_LEAVE");
    }

    [Fact]
    public async Task Rejected_Lines_Never_Block_The_Valid_Ones()
    {
        var unknown = Guid.NewGuid();

        var result = await RecordAsync(Night, PavA,
        [
            Entry("m1", true),
            Entry("m4", true),                              // un autre pavillon
            Entry("m2", true),
            Entry("m2", false),                             // doublon : la 1ʳᵉ occurrence l'emporte
            new AttendanceEntryInput(unknown, true, null),  // séjour inconnu
            Entry("attente", true),                         // interne sans lit
            Entry("demi", true)                             // demi-pensionnaire : ne dort pas à l'internat
        ]);

        (result.Saved, result.Rejected).Should().Be((2, 5));
        result.Lines.Where(l => l.Outcome == "Rejected").Select(l => l.Code)
            .Should().BeEquivalentTo(["NOT_IN_DORMITORY", "DUPLICATE_ENTRY", "NOT_IN_DORMITORY", "NOT_IN_DORMITORY", "NOT_IN_DORMITORY"]);

        var rows = await RowsAsync(Night);
        rows.Select(r => r.BoardingEnrollmentId).Should().BeEquivalentTo([Stays["m1"], Stays["m2"]]);
        rows.Single(r => r.BoardingEnrollmentId == Stays["m2"]).IsPresent.Should().BeTrue();
    }

    [Fact]
    public async Task Two_Simultaneous_First_Recordings_Leave_One_Row_And_Never_A_Raw_Error()
    {
        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(async _ =>
            {
                try
                {
                    await RecordAsync(Night, PavA, [Entry("m3", true)]);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }));

        // Deux issues légitimes : (1) les deux écritures se chevauchent → la seconde heurte l'index unique et reçoit
        // ATTENDANCE_CONFLICT ; (2) la seconde lit la ligne APRÈS le commit de la première → c'est une mise à jour
        // (upsert), donc AUCUNE erreur. Ce qui est interdit : toute autre erreur (brute) ou une seconde ligne.
        // `OnlyContain` ne convient pas : il échoue sur une liste VIDE, c.-à-d. précisément l'issue (2) — le test
        // échouait donc au hasard de l'ordonnancement (vu en CI le 11/10/2026).
        var errors = attempts.Where(e => e is not null).ToList();
        errors.Should().NotContain(
            e => !(e is BusinessRuleException) || ((BusinessRuleException)e).Code != "ATTENDANCE_CONFLICT",
            "toute erreur autre qu'un conflit métier serait une erreur brute exposée à l'utilisateur");
        errors.Count.Should().BeLessThan(2, "une seule des deux saisies simultanées peut perdre la course");
        (await RowsAsync(Night)).Should().ContainSingle();
    }

    [Fact]
    public async Task The_Sheet_Lists_Only_Boarders_With_A_Bed_In_The_Dormitory_And_Prefills_Leave_Absentees()
    {
        await AddLeaveAsync("m1", new DateOnly(2026, 10, 10));
        await RecordAsync(Today, PavA, [Entry("m2", true, "RAS")]);

        var sheet = await SheetAsync(Today, PavA);

        sheet.DormitoryName.Should().Be("Pavillon Oustaz Ahmad");
        sheet.Lines.Select(l => (l.RoomName, l.BedNumber)).Should().Equal(("Chambre 101", 1), ("Chambre 101", 2), ("Chambre 102", 1));
        sheet.Lines.Select(l => l.BoarderId).Should().Equal(Stays["m1"], Stays["m2"], Stays["m3"]);

        var m1 = sheet.Lines[0];
        (m1.OnLeave, m1.Recorded, m1.IsPresent, m1.Note).Should().Be((true, false, false, "En permission"));
        var m2 = sheet.Lines[1];
        (m2.OnLeave, m2.Recorded, m2.IsPresent, m2.Note).Should().Be((false, true, true, "RAS"));
        var m3 = sheet.Lines[2];
        (m3.OnLeave, m3.Recorded, m3.IsPresent, m3.Note).Should().Be((false, false, (bool?)null, (string?)null));
        sheet.Lines.Should().OnlyContain(l => l.StudentName.StartsWith("Élève m"));
    }

    [Fact]
    public async Task Another_School_Can_Neither_Read_Nor_Record_The_Dormitory()
    {
        var read = async () => await SheetAsync(Today, PavA, school: EcoleB);
        var write = async () => await RecordAsync(Today, PavA, [Entry("m1", true)], school: EcoleB);

        await read.Should().ThrowAsync<KeyNotFoundException>();
        await write.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_Sheet_Of_A_Future_Date_Is_Refused()
    {
        var future = async () => await SheetAsync(new DateOnly(2026, 10, 13), PavA);

        await future.Should().ThrowAsync<ValidationException>();
    }
}
