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

/// <summary>Page de pensionnaires (convention de pagination du Volume 4 §0.2).</summary>
public record PaginatedBoarders(IReadOnlyList<BoarderListItemDto> Items, int TotalCount, int Page, int PageSize);

public record AllowedExitPersonDto(string Name, string Relationship, string Phone);

/// <summary>
/// Fiche complète d'un pensionnaire. <see cref="MedicalNotes"/> est une donnée de santé d'un mineur : elle n'est renvoyée
/// qu'au Directeur et au Surveillant, et vaut <c>null</c> pour tout autre rôle (spec §5.2).
/// </summary>
public record BoarderDetailDto(
    BoarderListItemDto Boarder,
    string? MedicalNotes,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    IReadOnlyList<AllowedExitPersonDto> AllowedExitPersons);
