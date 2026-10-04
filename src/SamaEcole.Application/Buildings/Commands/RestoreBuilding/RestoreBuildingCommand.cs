using MediatR;

namespace SamaEcole.Application.Buildings.Commands.RestoreBuilding;

/// <summary>POST /api/v1/buildings/{id}/restore — restaure un bâtiment supprimé logiquement.</summary>
public record RestoreBuildingCommand(Guid Id) : IRequest<Unit>;
