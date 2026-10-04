using MediatR;

namespace SamaEcole.Application.Uniforms.Commands.DeleteUniformItem;

/// <summary>
/// Archive un modèle (suppression logique) et ses tailles. Refusé tant qu'un besoin EN ATTENTE l'utilise ; les tenues
/// déjà remises ne bloquent pas : leur fiche garde ses libellés figés.
/// </summary>
public record DeleteUniformItemCommand : IRequest<Unit>
{
    public Guid Id { get; init; }

    public uint RowVersion { get; init; }
}
