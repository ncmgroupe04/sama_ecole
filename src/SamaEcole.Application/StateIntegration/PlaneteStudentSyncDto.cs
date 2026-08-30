using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration;

/// <summary>
/// UNE ligne de la matrice « Planète Ready » — le format d'échange élève attendu par le SIMEN
/// (Volume 1 §23.2, Volume 4 §23).
///
/// Ce DTO est le CONTRAT D'ÉCHANGE, pas une vue interne : il est sérialisé tel quel en JSON, écrit
/// colonne par colonne en CSV, et transmis au relais API (<see cref="Common.Interfaces.ISimenBridgeService"/>).
/// Les trois sorties partagent CE type — c'est ce partage, et non une discipline de relecture, qui
/// garantit qu'un CSV et un JSON du même export portent les mêmes champs, dans le même ordre.
///
/// Règles de remplissage, non négociables :
///   • Aucune valeur inventée. Un champ que l'école n'a pas saisi part à <c>null</c> ; le CSV écrit
///     une cellule vide. Remplir un IEN manquant par le matricule interne, ou un lieu de naissance
///     absent par la ville de l'école, produirait un fichier plausible et faux — le pire des deux.
///   • <c>IenNumber</c> peut être null ou provisoire : <c>IsIenProvisional</c> tranche. Le
///     consommateur DOIT lire les deux ensemble.
///   • Aucune donnée financière. Le SIMEN reçoit un état civil scolaire, pas la situation de paiement
///     d'une famille — la faire sortir de l'établissement ne relève d'aucune obligation légale.
/// </summary>
public record PlaneteStudentSyncDto(
    // IEN officiel OU provisoire OU null — à lire conjointement avec IsIenProvisional ci-dessous.
    string? IenNumber,

    // Vrai si l'IEN a été fabriqué localement (algorithme de secours). Le champ existe pour que le
    // destinataire puisse REJETER ces lignes s'il ne les accepte pas — ce qui est son droit, et qu'il
    // ne pourrait pas exercer si nous transmettions un numéro provisoire sans le signaler.
    bool IsIenProvisional,

    // Matricule INTERNE de l'établissement : sans valeur pour le ministère, repris pour que l'école
    // puisse rapprocher le fichier transmis de ses propres listes.
    string Matricule,

    string LastName,
    string FirstNames,
    DateOnly BirthDate,
    string BirthPlace,

    // « M » / « F », tel que stocké — aucune traduction, le format national attend ces deux lettres.
    string Gender,

    string ClassroomName,

    // Niveau réglementaire (CI, CP, 6e, Terminale…) : la maille d'agrégation du ministère, et non le
    // nom de la classe, qui est propre à l'école (« 6e A », « 6e Bleue »).
    string Level,

    // Cycle de la classe, en clair (Maternelle / Primaire / Collège / Lycée).
    string Cycle,

    string SchoolYearLabel,

    // Redoublement du niveau cette année (Enrollment.IsRepeating) — colonne obligatoire du format.
    bool IsRepeating,

    // Tuteur : null si l'école ne l'a pas saisi, jamais remplacé par le nom de l'élève.
    string? GuardianName,
    string? GuardianPhone,

    // Code établissement national de l'école ÉMETTRICE, répété sur CHAQUE ligne — exigence du format,
    // qui prévoit qu'un lot puisse être scindé sans perdre son origine.
    string NationalSchoolCode);

/// <summary>
/// L'export Planète COMPLET : l'en-tête d'établissement, puis les lignes élèves.
///
/// L'en-tête n'est pas décoratif — le fichier est rejeté sans lui. Il est porté par ce DTO plutôt que
/// reconstruit par chaque sérialiseur, pour la même raison que ci-dessus : une seule source de vérité.
/// </summary>
public record PlaneteExportDto(
    string NationalSchoolCode,
    string SchoolName,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? SchoolDistrictCode,
    string SchoolYearLabel,

    // Horodatage de génération : ce qui distingue deux exports du même jour à l'arrivée.
    DateTimeOffset GeneratedAt,

    IReadOnlyList<PlaneteStudentSyncDto> Students)
{
    /// <summary>Effectif transmis — recompté à la sérialisation, jamais saisi séparément.</summary>
    public int StudentCount => Students.Count;

    /// <summary>
    /// Nombre de lignes portant un IEN PROVISOIRE. Affiché à l'utilisateur AVANT le téléchargement :
    /// une école doit savoir qu'elle s'apprête à transmettre 214 identifiants fabriqués, et pouvoir
    /// renoncer. Sans ce compteur, l'information n'existerait que dans le fichier, découverte trop tard.
    /// </summary>
    public int ProvisionalIenCount => Students.Count(s => s.IsIenProvisional);

    /// <summary>Nombre de lignes SANS aucun IEN — leur cellule partira vide.</summary>
    public int MissingIenCount => Students.Count(s => s.IenNumber is null);
}

/// <summary>Fichier d'export prêt à être servi — le type MIME découle du format, jamais saisi à part.</summary>
public record StateExportFile(byte[] Content, string FileName, StateExportFormat Format)
{
    public string ContentType => Format switch
    {
        StateExportFormat.Json => "application/json",
        StateExportFormat.Csv => "text/csv",
        _ => "application/octet-stream"
    };
}
