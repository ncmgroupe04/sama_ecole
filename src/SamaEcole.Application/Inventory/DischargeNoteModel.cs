namespace SamaEcole.Application.Inventory;

/// <summary>
/// Données prêtes à rendre pour la fiche de décharge (GET /inventory/assignments/{id}/pdf) — le
/// papier que le bénéficiaire signe en recevant des manuels ou du matériel.
///
/// Tout y est FIGÉ à la lecture, y compris le nom du bénéficiaire (voir
/// <c>ItemAssignment.BeneficiaryLabel</c>) : une décharge réimprimée doit être identique à celle qui
/// a été signée.
/// </summary>
public record DischargeNoteModel(
    string Reference,
    string SchoolName,
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? SchoolAddress,
    string? SchoolPhone,
    string BeneficiaryType,
    string BeneficiaryLabel,
    /// <summary>Classe de l'élève, ou fonction du personnel. Null si l'information n'existe pas — jamais inventée.</summary>
    string? BeneficiaryDetail,
    string ItemName,
    string? ItemCode,
    string CategoryName,
    int Quantity,
    string Condition,
    DateOnly AssignedOn,
    DateOnly? DueOn,
    string Status,
    DateOnly? ReturnedOn,
    int ReturnedQuantity,
    string? Notes);
