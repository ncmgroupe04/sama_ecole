using MediatR;

namespace SamaEcole.Application.Uniforms.Commands.CreateUniformVariant;

/// <summary>
/// Ajoute une taille à un modèle. Crée AUSSI, dans la même transaction, le lot d'inventaire qui portera son stock
/// (catégorie « Tenues » de l'école, créée si besoin) : le stock s'alimente ensuite depuis l'écran Inventaire.
/// </summary>
public record CreateUniformVariantCommand : IRequest<UniformVariantDto>
{
    public Guid UniformItemId { get; init; }

    public required string SizeLabel { get; init; }

    public int SortOrder { get; init; }
}
