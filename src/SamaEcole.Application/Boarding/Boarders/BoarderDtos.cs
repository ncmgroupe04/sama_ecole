using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding;

/// <summary>
/// Ligne d'un pensionnaire (séjour) : identité de l'élève, régime, lit/chambre/pavillon et dates. Les champs de lit, de
/// chambre et de pavillon sont nuls pour un demi-pensionnaire, un interne « en attente » ou un séjour clos.
/// <see cref="RowVersion"/> est le jeton xmin du SÉJOUR, à renvoyer pour un transfert ou une fin de séjour.
/// </summary>
public record BoarderListItemDto(
    Guid Id,
    Guid StudentId,
    Guid EnrollmentId,
    string StudentName,
    string Matricule,
    string ClassroomName,
    BoardingRegime Regime,
    bool IsActive,
    Guid? BedId,
    int? BedNumber,
    Guid? RoomId,
    string? RoomName,
    Guid? DormitoryId,
    string? DormitoryName,
    DateOnly StartDate,
    DateOnly? EndDate,
    uint RowVersion);
