using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Institutional;

/// <summary>
/// Rapport de rentrée scolaire destiné à l'Inspection de l'Éducation et de la Formation (Évolution N°7, canevas
/// statistique IEF). Trois tableaux : effectifs par classe, par tranche d'âge et par sexe (avec statut Nouveau /
/// Redoublant / Transféré et élèves hors tranche d'âge normale) ; taux de redoublement par niveau ; corps
/// professoral par discipline, par diplôme et par volume horaire hebdomadaire. Tout est un COMPTAGE : comme le
/// rapport STATEDUC, une donnée non saisie est comptée à part, jamais imputée.
/// </summary>
public record IefReportDto(
    string SchoolName,
    string? NationalSchoolCode,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string SchoolYearLabel,
    DateOnly AgeReferenceDate,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<AgeBucket> AgeColumns,
    IReadOnlyList<IefClassRow> Classes,
    IReadOnlyList<IefRepetitionRow> RepetitionByLevel,
    IReadOnlyList<IefDisciplineRow> TeachersByDiscipline,
    IReadOnlyList<IefDiplomaRow> TeachersByDiploma,
    IReadOnlyList<IefTeacherRow> Teachers)
{
    public int TotalStudents => Classes.Sum(c => c.Total);
    public int TotalGirls => Classes.Sum(c => c.Girls);
    public int TotalBoys => Classes.Sum(c => c.Boys);
    public int TotalRepeaters => Classes.Sum(c => c.Repeaters);
    public int TotalOutOfNorm => Classes.Sum(c => c.Early + c.Late);

    /// <summary>Pourcentage à une décimale ; null si le dénominateur est nul (« — », jamais « 0 % »).</summary>
    public static decimal? Rate(int part, int whole) =>
        whole == 0 ? null : Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero);
}

/// <param name="GradeLevel">Niveau lu sur le nom de la classe ; null si le nom ne le dit pas (aucune norme d'âge alors).</param>
/// <param name="Cells">Une cellule (Filles, Garçons) par colonne d'âge de <see cref="IefReportDto.AgeColumns"/>.</param>
/// <param name="UnknownAge">Élèves sans âge exploitable (date de naissance future ou invraisemblable).</param>
/// <param name="GenderNotReported">Élèves dont le sexe n'est ni « F » ni « G/M » : ni filles ni garçons.</param>
public record IefClassRow(
    string ClassroomName,
    string? GradeLevel,
    CycleType Cycle,
    int Girls,
    int Boys,
    int Total,
    int New,
    int Repeaters,
    int Transferred,
    int Early,
    int Late,
    string? NormLabel,
    IReadOnlyList<IefAgeCell> Cells,
    int UnknownAge,
    int GenderNotReported);

public record IefAgeCell(int Girls, int Boys);

/// <param name="Level">Niveau (CI… Terminale), à défaut le niveau/cycle saisi sur la classe.</param>
public record IefRepetitionRow(string Level, int Enrolled, int Repeaters, int RepeaterGirls, int RepeaterBoys)
{
    public decimal? RepetitionRate => IefReportDto.Rate(Repeaters, Enrolled);
}

/// <param name="WeeklyHours">Heures hebdomadaires planifiées à l'emploi du temps dans cette discipline, tous enseignants.</param>
public record IefDisciplineRow(string Discipline, int Teachers, int Men, int Women, decimal WeeklyHours);

public record IefDiplomaRow(AcademicQualification Academic, ProfessionalQualification Professional, int Men, int Women, int GenderNotReported)
{
    public int Count => Men + Women + GenderNotReported;
}

/// <param name="Disciplines">Matières enseignées : celles de l'emploi du temps, à défaut les matières qualifiées.</param>
/// <param name="WeeklyHours">Volume horaire hebdomadaire planifié à l'emploi du temps (toutes classes).</param>
public record IefTeacherRow(
    string FullName,
    string? Gender,
    IReadOnlyList<string> Disciplines,
    AcademicQualification Academic,
    ProfessionalQualification Professional,
    decimal WeeklyHours);
