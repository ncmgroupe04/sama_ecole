using SamaEcole.Application.Features.Schedules;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.RollBook.Pdf;

/// <summary>
/// Langue de rendu du cahier imprimé : <c>French</c> (gauche à droite), <c>Arabic</c> (document entièrement de
/// droite à gauche) ou <c>Bilingual</c> (grille de gauche à droite, en-têtes et libellés français / arabe côte à
/// côte). L'arabe et le bilingue n'existent que si l'école active le module Franco-Arabe.
/// </summary>
public enum RollBookPdfLanguage
{
    French = 0,
    Arabic = 1,
    Bilingual = 2
}

public sealed record RollBookPdfDay(
    DateOnly Date,
    bool IsClassDay,
    DayOffReason? OffReason,
    string? ClosureLabel,
    string? ClosureLabelAr);

public sealed record RollBookPdfCell(DateOnly Date, HalfDay Half, RollBookMark Mark);

public sealed record RollBookPdfStudent(
    string FullName,
    string? FullNameAr,
    string Matricule,
    IReadOnlyList<RollBookPdfCell> Cells,
    AttendanceCounts Counts);

/// <summary>Un mois du cahier : les jours, les cases renseignées de chaque élève, et ce qui était attendu.</summary>
public sealed record RollBookPdfMonth(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<RollBookPdfDay> Days,
    IReadOnlyList<HalfDay> ActiveHalves,
    IReadOnlyList<RollBookPdfStudent> Students,
    int ExpectedHalfDaysPerStudent)
{
    public int ClassDays => Days.Count(d => d.IsClassDay);

    public AttendanceCounts Totals => AttendanceCounts.Sum(Students.Select(s => s.Counts));

    public int ExpectedHalfDays => ExpectedHalfDaysPerStudent * Students.Count;
}

/// <summary>Récapitulatif trimestriel : les compteurs d'un élève mois par mois, puis sur toute la période.</summary>
public sealed record RollBookPdfTermRow(
    string FullName,
    string? FullNameAr,
    IReadOnlyList<AttendanceCounts> PerMonth,
    AttendanceCounts Total);

/// <summary>Une classe du niveau, pour la page de synthèse par niveau.</summary>
public sealed record RollBookPdfLevelClass(string Name, int StudentCount, AttendanceCounts Counts, int ExpectedHalfDays);

/// <summary>
/// Tout ce qu'imprime le cahier de roulement, sans règle métier restante : le document ne calcule rien, il met en
/// page. Un mois = le cahier mensuel ; plusieurs mois + <see cref="TermRows"/> = le cahier d'une période d'évaluation
/// (grilles mensuelles, récapitulatif, puis synthèse du niveau si l'utilisateur y a droit).
/// </summary>
public sealed record RollBookPdfModel(
    RollBookPdfLanguage Language,
    string SchoolName,
    string? SchoolNameAr,
    string? InspectionAcademie,
    string? InspectionAcademieAr,
    string? InspectionEducationFormation,
    string? InspectionEducationFormationAr,
    string SchoolYearLabel,
    string ClassroomName,
    string Level,
    SectionPattern? Pattern,
    IReadOnlyList<RollBookPdfMonth> Months,
    string? TermLabel,
    IReadOnlyList<RollBookPdfTermRow> TermRows,
    IReadOnlyList<RollBookPdfLevelClass> LevelClasses,
    DateOnly PrintedOn)
{
    public bool IsTerm => TermLabel is not null;

    /// <summary>Demi-journées attendues par élève sur toute la période imprimée.</summary>
    public int ExpectedHalfDaysPerStudent => Months.Sum(m => m.ExpectedHalfDaysPerStudent);

    public int StudentCount => Months.Count == 0 ? 0 : Months[0].Students.Count;

    public AttendanceCounts ClassTotals => AttendanceCounts.Sum(Months.Select(m => m.Totals));

    public int ClassExpectedHalfDays => Months.Sum(m => m.ExpectedHalfDays);
}

/// <summary>Images facultatives : leur absence n'empêche jamais l'émission du cahier (même règle que le logo des autres documents).</summary>
public sealed record RollBookPdfAssets(byte[]? Logo, byte[]? DirectorSignature, byte[]? Stamp)
{
    public static readonly RollBookPdfAssets None = new(null, null, null);

    public RollBookPdfAssets WithoutImages() => None;

    public bool HasImages => Logo is not null || DirectorSignature is not null || Stamp is not null;
}
