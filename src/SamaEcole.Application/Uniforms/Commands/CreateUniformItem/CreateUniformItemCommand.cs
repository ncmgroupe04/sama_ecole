using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.CreateUniformItem;

/// <summary>Crée un modèle de tenue. Le stock vient des tailles (<c>CreateUniformVariantCommand</c>), pas du modèle.</summary>
public record CreateUniformItemCommand : IRequest<UniformItemDto>
{
    public required string Name { get; init; }

    public UniformKind Kind { get; init; } = UniformKind.Uniforme;

    public UniformGender Gender { get; init; } = UniformGender.Mixte;

    public CycleType? Cycle { get; init; }

    /// <summary>Frais optionnel qui facture ce modèle ; facultatif — sans lui, toute remise est « non facturée ».</summary>
    public Guid? FeeCategoryId { get; init; }

    /// <summary>Unités couvertes par UNE ligne de frais (au moins 1).</summary>
    public int QuantityIncluded { get; init; } = 1;

    public string? Notes { get; init; }
}
