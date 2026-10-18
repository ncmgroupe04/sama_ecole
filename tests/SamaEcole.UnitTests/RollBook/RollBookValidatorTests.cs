using FluentAssertions;
using SamaEcole.Application.RollBook.Commands.UpsertRollBookEntries;
using SamaEcole.Application.RollBook.Queries.GetRollBookGrid;
using SamaEcole.Application.RollBook.Queries.GetRollBookStats;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.RollBook;

/// <summary>Cahier de roulement (Bloc 5b) : validation des requêtes et du lot de saisie.</summary>
public class RollBookValidatorTests
{
    private static readonly Guid Classe = Guid.NewGuid();
    private static readonly Guid Eleve = Guid.NewGuid();
    private static readonly DateOnly Jour = new(2026, 11, 3);

    // -- Grille ----------------------------------------------------------------------------------------

    private readonly GetRollBookGridQueryValidator _grid = new();

    [Fact]
    public void A_Valid_Grid_Query_Passes()
        => _grid.Validate(new GetRollBookGridQuery(Classe, "2026-11")).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("novembre")]
    public void A_Bad_Month_Fails(string month)
        => _grid.Validate(new GetRollBookGridQuery(Classe, month)).IsValid.Should().BeFalse();

    [Fact]
    public void A_Missing_Classroom_Fails()
        => _grid.Validate(new GetRollBookGridQuery(Guid.Empty, "2026-11")).IsValid.Should().BeFalse();

    // -- Statistiques ----------------------------------------------------------------------------------

    private readonly GetRollBookStatsQueryValidator _stats = new();

    [Fact]
    public void A_Class_Scope_With_A_Month_Passes()
        => _stats.Validate(new GetRollBookStatsQuery("class", Classe, "2026-11", null)).IsValid.Should().BeTrue();

    [Fact]
    public void A_Level_Scope_With_A_Term_Passes_Without_A_Classroom()
        => _stats.Validate(new GetRollBookStatsQuery("level", null, null, Guid.NewGuid())).IsValid.Should().BeTrue();

    [Fact]
    public void A_Class_Scope_Needs_A_Classroom()
        => _stats.Validate(new GetRollBookStatsQuery("class", null, "2026-11", null)).IsValid.Should().BeFalse();

    [Fact]
    public void An_Unknown_Scope_Fails()
        => _stats.Validate(new GetRollBookStatsQuery("school", null, "2026-11", null)).IsValid.Should().BeFalse();

    [Fact]
    public void Exactly_One_Of_Month_Or_Term_Is_Required()
    {
        _stats.Validate(new GetRollBookStatsQuery("level", null, null, null)).IsValid.Should().BeFalse("aucune période");
        _stats.Validate(new GetRollBookStatsQuery("level", null, "2026-11", Guid.NewGuid())).IsValid.Should().BeFalse("deux périodes");
    }

    [Fact]
    public void A_Bad_Month_In_Stats_Fails()
        => _stats.Validate(new GetRollBookStatsQuery("level", null, "2026-99", null)).IsValid.Should().BeFalse();

    // -- Lot de saisie ---------------------------------------------------------------------------------

    private readonly UpsertRollBookEntriesCommandValidator _upsert = new();

    private static RollBookEntryInput Entry(
        AttendanceStatus? status = AttendanceStatus.Present, int minutes = 0, string? reason = null,
        HalfDay half = HalfDay.Morning, Guid? student = null, DateOnly? date = null) =>
        new(student ?? Eleve, date ?? Jour, half, status, minutes, reason);

    private static UpsertRollBookEntriesCommand Command(params RollBookEntryInput[] entries) => new(Classe, entries);

    [Fact]
    public void A_Valid_Batch_Passes()
        => _upsert.Validate(Command(
                Entry(),
                Entry(AttendanceStatus.Late, 12, "Bus en panne", HalfDay.Afternoon),
                Entry(AttendanceStatus.JustifiedAbsence, reason: "Certificat", student: Guid.NewGuid())))
            .IsValid.Should().BeTrue();

    [Fact]
    public void A_Null_Status_Means_Revert_And_Is_Valid()
        => _upsert.Validate(Command(Entry(status: null))).IsValid.Should().BeTrue();

    [Fact]
    public void An_Empty_Batch_Fails()
        => _upsert.Validate(Command()).IsValid.Should().BeFalse();

    [Fact]
    public void A_Batch_Over_The_Limit_Fails()
    {
        var entries = Enumerable.Range(0, UpsertRollBookEntriesCommandValidator.MaxEntries + 1)
            .Select(i => Entry(student: Guid.NewGuid(), date: Jour))
            .ToArray();

        _upsert.Validate(Command(entries)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void The_Same_Cell_Twice_In_A_Batch_Fails()
        => _upsert.Validate(Command(Entry(), Entry(AttendanceStatus.UnjustifiedAbsence))).IsValid.Should().BeFalse();

    [Fact]
    public void Morning_And_Afternoon_Of_The_Same_Day_Are_Two_Different_Cells()
        => _upsert.Validate(Command(Entry(half: HalfDay.Morning), Entry(half: HalfDay.Afternoon))).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(AttendanceStatus.Present)]
    [InlineData(AttendanceStatus.JustifiedAbsence)]
    [InlineData(AttendanceStatus.UnjustifiedAbsence)]
    public void Late_Minutes_Only_Make_Sense_For_A_Late_Status(AttendanceStatus status)
        => _upsert.Validate(Command(Entry(status, minutes: 10))).IsValid.Should().BeFalse();

    [Fact]
    public void Late_Minutes_Must_Stay_Within_Bounds()
    {
        _upsert.Validate(Command(Entry(AttendanceStatus.Late, 601))).IsValid.Should().BeFalse();
        _upsert.Validate(Command(Entry(AttendanceStatus.Late, -1))).IsValid.Should().BeFalse();
        _upsert.Validate(Command(Entry(AttendanceStatus.Late, 0))).IsValid.Should().BeTrue("retard sans durée précisée");
        _upsert.Validate(Command(Entry(AttendanceStatus.Late, 600))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Too_Long_Or_Html_Reason_Fails()
    {
        _upsert.Validate(Command(Entry(reason: new string('x', 301)))).IsValid.Should().BeFalse();
        _upsert.Validate(Command(Entry(reason: "<script>alert(1)</script>"))).IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Missing_Student_Fails()
        => _upsert.Validate(Command(Entry(student: Guid.Empty))).IsValid.Should().BeFalse();

    [Fact]
    public void A_Missing_Classroom_Fails_The_Batch()
        => _upsert.Validate(new UpsertRollBookEntriesCommand(Guid.Empty, [Entry()])).IsValid.Should().BeFalse();

    [Fact]
    public void An_Unknown_Status_Or_Half_Day_Fails()
    {
        _upsert.Validate(Command(Entry((AttendanceStatus)99))).IsValid.Should().BeFalse();
        _upsert.Validate(Command(Entry(half: (HalfDay)9))).IsValid.Should().BeFalse();
    }
}
