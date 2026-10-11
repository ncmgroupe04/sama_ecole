using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Leaves;

/// <summary>
/// Une sortie ou permission d'un pensionnaire. <see cref="Status"/> est CALCULÉ à partir des dates (spec N6), jamais stocké.
/// <see cref="IsCompanionUnlisted"/> est figé à la déclaration (spec Q5). <see cref="RowVersion"/> est le jeton xmin de la
/// sortie, à renvoyer pour enregistrer le retour. Le pavillon et la chambre sont ceux du séjour ACTUEL (nuls s'il est clos
/// ou sans lit).
/// </summary>
public record BoardingLeaveDto(
    Guid Id,
    Guid BoarderId,
    Guid StudentId,
    string StudentName,
    string Matricule,
    string? DormitoryName,
    string? RoomName,
    BoardingLeaveReason Reason,
    string? ReasonDetail,
    DateOnly LeaveDate,
    DateOnly ExpectedReturnDate,
    DateOnly? ActualReturnDate,
    string AccompaniedBy,
    bool IsCompanionUnlisted,
    LeaveStatus Status,
    uint RowVersion);

/// <summary>Page de sorties (convention de pagination du Volume 4 §0.2).</summary>
public record PaginatedLeaves(IReadOnlyList<BoardingLeaveDto> Items, int TotalCount, int Page, int PageSize);
