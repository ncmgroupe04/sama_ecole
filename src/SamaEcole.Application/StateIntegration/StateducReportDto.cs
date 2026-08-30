using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration;

/// <summary>
/// Rapport annuel STATEDUC — l'état statistique que chaque établissement transmet au ministère en fin
/// d'année (Volume 1 §23.3, ticket JGK-M04). Quatre tableaux réglementaires : effectifs par niveau,
/// pyramide des âges, ratios filles/garçons, qualification du personnel enseignant.
///
/// TOUT y est un COMPTAGE, jamais une estimation. Une case que les données ne permettent pas de
/// remplir est comptée dans une ligne « non renseigné » explicite plutôt qu'imputée à la valeur la
/// plus probable : un formulaire officiel dont 12 % des enseignants seraient rangés d'office en
/// « sans diplôme » parce que l'école n'a pas saisi leurs titres est un faux, et c'est l'école qui
/// en répondrait, pas nous.
///
/// PÉRIMÈTRE : les élèves comptés sont ceux qui ont une inscription NON annulée sur l'année scolaire
/// demandée — pas les élèves « actuellement en base », qui inclueraient les partis et excluraient les
/// arrivés. Cette distinction change l'effectif de plusieurs pour cent dans une école ordinaire.
/// </summary>
public record StateducReportDto(
    // ---- En-tête réglementaire du formulaire ---------------------------------------------------
    string SchoolName,
    string? NationalSchoolCode,
    string? MinistryAuthorizationNumber,
    string? SchoolDistrictCode,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? GpsCoordinates,
    string? Address,
    string? Phone,
    string? Email,

    string SchoolYearLabel,

    // Date d'ARRÊTÉ des comptages. Le rapport est une photographie : régénéré un mois plus tard il
    // donnera d'autres chiffres, et c'est cette date — pas celle de l'impression — qui explique
    // l'écart à qui compare deux exemplaires.
    DateOnly ObservationDate,
    DateTimeOffset GeneratedAt,

    // ---- Tableau 1 — Effectifs par niveau ------------------------------------------------------
    IReadOnlyList<StateducLevelEnrollmentRow> EnrollmentsByLevel,

    // ---- Tableau 2 — Pyramide des âges ---------------------------------------------------------
    IReadOnlyList<StateducAgeBracketRow> AgePyramid,

    // ---- Tableau 3 — Personnel enseignant ------------------------------------------------------
    IReadOnlyList<StateducTeacherQualificationRow> TeacherQualifications,
    IReadOnlyList<StateducTeacherStatusRow> TeacherStatuses,

    // ---- Tableau 4 — Infrastructures (module Bâtiments & Salles) -------------------------------
    int ClassroomCount,
    int PhysicalRoomCount,
    int BuildingCount)
{
    /// <summary>Effectif total, recalculé depuis le tableau 1 — jamais un compteur saisi en parallèle.</summary>
    public int TotalStudents => EnrollmentsByLevel.Sum(r => r.Total);

    public int TotalGirls => EnrollmentsByLevel.Sum(r => r.Girls);
    public int TotalBoys => EnrollmentsByLevel.Sum(r => r.Boys);
    public int TotalRepeaters => EnrollmentsByLevel.Sum(r => r.Repeaters);

    /// <summary>
    /// Part de filles sur l'ensemble de l'établissement, en POURCENTAGE (0–100), arrondie à une
    /// décimale. Null — et non zéro — quand l'effectif est nul : « 0 % de filles » dans une école
    /// vide est une affirmation, « — » est la vérité.
    /// </summary>
    public decimal? GirlsRatio => Ratio(TotalGirls, TotalStudents);

    public decimal? BoysRatio => Ratio(TotalBoys, TotalStudents);

    /// <summary>Effectif enseignant total, recalculé depuis le tableau de qualification.</summary>
    public int TotalTeachers => TeacherQualifications.Sum(r => r.Count);

    /// <summary>
    /// Nombre d'enseignants QUALIFIÉS au sens du ministère : porteurs d'un diplôme PROFESSIONNEL
    /// (CEAP, CAP, CAEM, CAES). Un Master sans titre pédagogique n'est pas qualifié — c'est la
    /// définition officielle, pas la nôtre, et l'inverser flatterait l'école au prix d'un faux.
    /// </summary>
    public int QualifiedTeachers => TeacherQualifications
        .Where(r => r.ProfessionalQualification is
            ProfessionalQualification.CEAP or ProfessionalQualification.CAP
            or ProfessionalQualification.CAEM or ProfessionalQualification.CAES)
        .Sum(r => r.Count);

    /// <summary>
    /// Enseignants dont la qualification n'a jamais été saisie. Publié SÉPARÉMENT du taux de
    /// qualification, et non fondu dedans : c'est la seule façon pour le lecteur de savoir si un taux
    /// de 40 % décrit l'école ou l'état de sa saisie.
    /// </summary>
    public int UnreportedQualificationTeachers => TeacherQualifications
        .Where(r => r.ProfessionalQualification == ProfessionalQualification.NonRenseigne)
        .Sum(r => r.Count);

    /// <summary>
    /// Enseignants dont le GENRE n'a pas été saisi. Publié pour la même raison que le compteur
    /// ci-dessus : le formulaire officiel ventile tout le personnel en Hommes/Femmes, et le lecteur
    /// doit pouvoir constater que la somme des deux colonnes ne fait pas l'effectif total — plutôt que
    /// de découvrir un écart inexpliqué en additionnant lui-même.
    /// </summary>
    public int TeachersWithoutGender => TeacherQualifications.Sum(r => r.GenderNotReported);

    /// <summary>Taux d'encadrement : élèves par enseignant. Null si aucun enseignant n'est enregistré.</summary>
    public decimal? StudentsPerTeacher => TotalTeachers == 0
        ? null
        : Math.Round((decimal)TotalStudents / TotalTeachers, 1, MidpointRounding.AwayFromZero);

    /// <summary>Élèves par salle de classe. Null si l'école n'a déclaré aucune classe.</summary>
    public decimal? StudentsPerClassroom => ClassroomCount == 0
        ? null
        : Math.Round((decimal)TotalStudents / ClassroomCount, 1, MidpointRounding.AwayFromZero);

    /// <summary>Élèves sans IEN — la ligne que l'IEF regarde en premier (Volume 1 §23.1).</summary>
    public int StudentsWithoutIen => EnrollmentsByLevel.Sum(r => r.WithoutIen);

    /// <summary>
    /// Pourcentage (0–100) à une décimale, ou <c>null</c> quand le dénominateur est nul — « 0 % de
    /// filles » dans une école vide est une affirmation, « — » est la vérité. <c>public</c> pour être
    /// réutilisé tel quel par les générateurs PDF/Excel (Infrastructure) : une seule définition de
    /// « comment se calcule un ratio », partagée avec les lignes de détail ci-dessous.
    /// </summary>
    public static decimal? Ratio(int part, int whole) => whole == 0
        ? null
        : Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Une ligne du tableau des effectifs : un NIVEAU réglementaire (CI, CP, 6e…), et non une classe.
/// Le ministère agrège par niveau ; publier « 6e A : 42 » et « 6e B : 39 » l'obligerait à refaire
/// notre travail, avec le risque d'erreur que cela comporte.
/// </summary>
public record StateducLevelEnrollmentRow(
    string Level,
    string Cycle,
    int Boys,
    int Girls,

    // Redoublants du niveau — colonne réglementaire distincte, jamais déduite d'un écart d'âge.
    int Repeaters,

    // Élèves du niveau sans IEN renseigné : le détail de StateducReportDto.StudentsWithoutIen.
    int WithoutIen,

    // Nombre de classes ouvertes sur ce niveau (une 6e à trois divisions compte 3).
    int ClassroomCount)
{
    public int Total => Boys + Girls;

    /// <summary>Part de filles du niveau. Null si le niveau est vide — voir la règle du DTO parent.</summary>
    public decimal? GirlsRatio => StateducReportDto.Ratio(Girls, Total);

    /// <summary>Effectif moyen par division du niveau. Null si aucune classe n'y est ouverte.</summary>
    public decimal? AverageClassSize => ClassroomCount == 0
        ? null
        : Math.Round((decimal)Total / ClassroomCount, 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Une tranche de la pyramide des âges. L'âge est calculé à la <c>ObservationDate</c> du rapport, PAS
/// à la date d'impression : deux tirages du même rapport à six mois d'écart doivent donner la même
/// pyramide, sans quoi elle ne serait comparable à rien.
///
/// La tranche <c>Age = null</c> recueille les élèves dont la date de naissance est absurde (postérieure
/// à l'observation, ou antérieure de plus de 30 ans) : ils existent en base après un import, et les
/// ranger dans une vraie tranche déplacerait silencieusement l'effectif d'une case à l'autre.
/// </summary>
public record StateducAgeBracketRow(int? Age, int Boys, int Girls)
{
    public int Total => Boys + Girls;

    /// <summary>Libellé de la tranche pour le formulaire officiel — « Âge non déterminé » si null.</summary>
    public string Label => Age is { } age ? $"{age} ans" : "Âge non déterminé";
}

/// <summary>
/// Une ligne du tableau de qualification : le CROISEMENT diplôme académique × diplôme professionnel,
/// tel que l'exige le formulaire. Les deux ne se déduisent pas l'un de l'autre — c'est tout l'objet
/// de la statistique ministérielle que de mesurer l'écart entre les deux.
///
/// <c>GenderNotReported</c> est une TROISIÈME colonne, absente du formulaire officiel qui n'en prévoit
/// que deux. Elle existe parce que <c>Teacher.Gender</c> est nullable : sans elle, les fiches sans
/// genre saisi devraient être imputées à l'une des deux colonnes réglementaires, ce qui serait faux et
/// indétectable. Le document imprimé la présente comme un reste à saisir, jamais fondue dans les
/// effectifs déclarés.
/// </summary>
public record StateducTeacherQualificationRow(
    AcademicQualification AcademicQualification,
    ProfessionalQualification ProfessionalQualification,
    int Men,
    int Women,
    int GenderNotReported)
{
    public int Count => Men + Women + GenderNotReported;
}

/// <summary>
/// Répartition du personnel enseignant par statut administratif (Fonctionnaire, Vacataire…). Même
/// troisième colonne, pour la même raison — voir <see cref="StateducTeacherQualificationRow"/>.
/// </summary>
public record StateducTeacherStatusRow(
    TeacherCivilServiceStatus Status,
    int Men,
    int Women,
    int GenderNotReported)
{
    public int Count => Men + Women + GenderNotReported;
}

/// <summary>Rapport STATEDUC rendu en fichier (PDF A4 paysage ou classeur .xlsx).</summary>
public record StateducReportFile(byte[] Content, string FileName, string ContentType);
