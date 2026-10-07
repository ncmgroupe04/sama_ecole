using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Dormitories.UpdateDormitory;

/// <summary>
/// PUT /api/v1/boarding/dormitories/{id}. <paramref name="RowVersion"/> : verrouillage optimiste (règle #5).
/// Le genre d'un pavillon qui héberge des pensionnaires ne peut pas changer.
/// </summary>
public record UpdateDormitoryCommand(
    Guid Id, string Name, DormitoryGender Gender, string? SupervisorName, string? SupervisorPhone,
    Guid? SupervisorUserId, string? Notes, uint RowVersion) : IRequest<DormitoryDto>;
