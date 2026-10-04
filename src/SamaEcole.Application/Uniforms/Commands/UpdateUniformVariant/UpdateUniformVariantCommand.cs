using MediatR;

namespace SamaEcole.Application.Uniforms.Commands.UpdateUniformVariant;

/// <summary>Renomme ou réordonne une taille ; le lot d'inventaire en suit le libellé. Aucun jeton : rien d'écrasable ici.</summary>
public record UpdateUniformVariantCommand : IRequest<UniformVariantDto>
{
    public Guid Id { get; init; }

    public required string SizeLabel { get; init; }

    public int SortOrder { get; init; }
}
