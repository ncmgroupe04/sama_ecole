using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Dormitories.CreateDormitory;

/// <summary>
/// POST /api/v1/boarding/dormitories — crée un pavillon. Le SchoolId n'est PAS ici : il est lu dans le JWT via
/// ITenantProvider (AGENTS.md règle #10). <see cref="DormitoryGender.Mixte"/> est refusé : il n'existe que pour la
/// reprise des anciens dortoirs. Si <see cref="SupervisorUserId"/> est fourni, le nom du surveillant est celui du
/// compte (jamais une copie stockée).
/// </summary>
public record CreateDormitoryCommand : IRequest<DormitoryDto>
{
    public required string Name { get; init; }
    public DormitoryGender Gender { get; init; }
    public string? SupervisorName { get; init; }
    public string? SupervisorPhone { get; init; }
    public Guid? SupervisorUserId { get; init; }
    public string? Notes { get; init; }
}
