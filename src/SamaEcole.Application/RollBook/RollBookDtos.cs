using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.RollBook;

/// <summary>Compteurs et taux (spec §2.4). Les taux sont null tant qu'aucune demi-journée n'est renseignée.</summary>
public sealed record RollBookCountsDto(
    int Present,
    int Late,
    int PartialAbsence,
    int JustifiedAbsence,
    int UnjustifiedAbsence,
    int Recorded,
    int LateMinutes,
    decimal? PresenceRate,
    decimal? AbsenceRate,
    decimal? AssiduityRate,
    decimal? Coverage)
{
    public static RollBookCountsDto From(AttendanceCounts counts, int expectedHalfDays) => new(
        counts.Present, counts.Late, counts.PartialAbsence, counts.JustifiedAbsence, counts.UnjustifiedAbsence,
        counts.Recorded, counts.LateMinutes,
        counts.PresenceRate, counts.AbsenceRate, counts.AssiduityRate, counts.Coverage(expectedHalfDays));
}

public sealed record RollBookDayDto(
    DateOnly Date,
    int DayOfWeek,
    bool IsClassDay,
    string? OffReason,
    string? ClosureLabel,
    string? ClosureLabelAr);

public sealed record RollBookCellDto(
    DateOnly Date,
    string HalfDay,
    string Mark,
    string Source,
    int LateMinutes,
    string? Reason,
    Guid? EntryId);

public sealed record RollBookStudentDto(
    Guid StudentId,
    string Matricule,
    string FullName,
    string? FullNameAr,
    IReadOnlyList<RollBookCellDto> Cells,
    RollBookCountsDto Counts);

/// <summary>Grille mensuelle d'une classe : jours, cases renseignées (le reste est « non renseigné »), totaux.</summary>
public sealed record RollBookGridDto(
    Guid ClassroomId,
    string ClassroomName,
    string Level,
    Guid SchoolYearId,
    string SchoolYearLabel,
    string Month,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<RollBookDayDto> Days,
    IReadOnlyList<string> ActiveHalves,
    int ClassDays,
    int UnplacedSessions,
    IReadOnlyList<RollBookStudentDto> Students,
    RollBookCountsDto Totals)
{
    public static RollBookGridDto Create(ClassRollBook book, SamaEcole.Domain.Entities.SchoolYear year, string month, DateOnly from, DateOnly to) => new(
        book.ClassroomId,
        book.Name,
        book.Level,
        year.Id,
        year.Label,
        month,
        from,
        to,
        book.Days.Select(d => new RollBookDayDto(
            d.Date, (int)d.Date.DayOfWeek, d.IsClassDay, d.OffReason?.ToString(), d.Closure?.Label, d.Closure?.LabelAr)).ToList(),
        book.ActiveHalves.Select(h => h.ToString()).ToList(),
        book.ClassDayCount,
        book.UnplacedSessions,
        book.Students.Select(s => new RollBookStudentDto(
            s.Student.Id, s.Student.Matricule, s.Student.FullName, s.Student.FullNameAr,
            s.Cells.Select(c => new RollBookCellDto(
                c.Date, c.Half.ToString(), c.Cell.Mark.ToString(), c.Cell.Source.ToString(),
                c.Cell.LateMinutes, c.Cell.Reason, c.Cell.EntryId)).ToList(),
            RollBookCountsDto.From(s.Counts, book.ExpectedHalfDaysPerStudent))).ToList(),
        RollBookCountsDto.From(book.Totals, book.ExpectedHalfDays));
}

public sealed record RollBookStudentStatsDto(
    Guid StudentId, string Matricule, string FullName, string? FullNameAr, RollBookCountsDto Counts);

public sealed record RollBookClassStatsDto(
    Guid ClassroomId, string Name, string Level, int StudentCount, int ClassDays, RollBookCountsDto Counts);

public sealed record RollBookLevelStatsDto(string Level, int ClassCount, int StudentCount, RollBookCountsDto Counts);

/// <summary>
/// Statistiques d'assiduité d'une période. Portée « class » : les élèves de la classe, plus la classe et son
/// niveau. Portée « level » : toutes les classes de l'école, regroupées par niveau (compteurs sommés).
/// </summary>
public sealed record RollBookStatsDto(
    string Scope,
    DateOnly From,
    DateOnly To,
    Guid SchoolYearId,
    string SchoolYearLabel,
    IReadOnlyList<RollBookStudentStatsDto> Students,
    IReadOnlyList<RollBookClassStatsDto> Classes,
    IReadOnlyList<RollBookLevelStatsDto> Levels,
    RollBookCountsDto Totals);

/// <summary>Résultat d'un enregistrement en lot : cases créées, modifiées, et remises au statut calculé.</summary>
public sealed record UpsertRollBookEntriesResult(int Created, int Updated, int Reverted);
