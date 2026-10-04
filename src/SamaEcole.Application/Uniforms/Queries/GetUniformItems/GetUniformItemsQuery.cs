using MediatR;

namespace SamaEcole.Application.Uniforms.Queries.GetUniformItems;

/// <summary>Le catalogue de l'école : modèles, tailles et stock vivant de chaque taille.</summary>
public record GetUniformItemsQuery : IRequest<IReadOnlyList<UniformItemDto>>;
