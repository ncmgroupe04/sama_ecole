namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Charge utile du reçu d'inscription (ticket JGK-E01), renvoyée aussi bien à la création qu'à la
/// relecture GET /enrollments/{id}/receipt. Elle porte tout ce que la référence de design impose
/// (docs/design-references/README.md §1) : en-tête établissement, identité de l'élève, tableau des
/// frais et total. Le reçu PDF pixel-fidèle est le ticket JGK-E02 ; cette DTO l'alimentera aussi.
///
/// La mention obligatoire (AGENTS.md règle #12) n'est pas transportée ici : c'est un texte constant,
/// figé côté vue/PDF pour qu'aucun appelant ne puisse l'altérer ou l'omettre.
/// </summary>
public record EnrollmentReceiptDto(
    Guid EnrollmentId,
    string SchoolName,
    string? SchoolPhone,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string ClassroomLevel,
    string SchoolYearLabel,
    string Type,
    string Status,
    DateTimeOffset EnrolledAt,
    IReadOnlyList<EnrollmentFeeLineDto> Lines,
    decimal TotalDue);

/// <summary>
/// Une ligne du tableau « Désignation des frais / Montant » du reçu. <paramref name="Months"/> vaut 1
/// pour un frais ponctuel et le nombre de mensualités pour une scolarité, ce qui permet à la vue
/// d'afficher « Mensualité (× 9) » sans recalculer quoi que ce soit.
/// </summary>
public record EnrollmentFeeLineDto(
    string Designation,
    bool IsRecurring,
    decimal UnitAmount,
    int Months,
    decimal LineTotal);
