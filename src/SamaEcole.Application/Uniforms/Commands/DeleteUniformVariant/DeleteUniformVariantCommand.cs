using MediatR;

namespace SamaEcole.Application.Uniforms.Commands.DeleteUniformVariant;

/// <summary>
/// Archive une taille (suppression logique). Refusé tant qu'un besoin EN ATTENTE la vise. Le lot d'inventaire et son
/// journal restent : l'archivage d'une taille n'efface aucun stock.
/// </summary>
public record DeleteUniformVariantCommand : IRequest<Unit>
{
    public Guid Id { get; init; }
}
