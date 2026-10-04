using MediatR;

namespace SamaEcole.Application.Uniforms.Commands.CancelStudentUniform;

/// <summary>
/// Annule une ligne encore à remettre. Jamais d'effacement : la ligne passe au statut « annulée » et libère la place
/// de l'unicité active. Une tenue déjà remise se retourne (mouvement de stock inverse), elle ne s'annule pas.
/// </summary>
public record CancelStudentUniformCommand : IRequest<Unit>
{
    public Guid Id { get; init; }

    public uint RowVersion { get; init; }
}
