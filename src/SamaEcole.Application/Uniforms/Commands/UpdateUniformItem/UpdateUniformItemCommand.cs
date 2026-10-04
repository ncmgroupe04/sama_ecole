using MediatR;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.UpdateUniformItem;

/// <summary>Remplace les champs d'un modèle. <c>RowVersion</c> : le jeton xmin lu avec le modèle (409 s'il est périmé).</summary>
public record UpdateUniformItemCommand : IRequest<UniformItemDto>
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public UniformKind Kind { get; init; } = UniformKind.Uniforme;

    public UniformGender Gender { get; init; } = UniformGender.Mixte;

    public CycleType? Cycle { get; init; }

    public Guid? FeeCategoryId { get; init; }

    public int QuantityIncluded { get; init; } = 1;

    public string? Notes { get; init; }

    public uint RowVersion { get; init; }
}
