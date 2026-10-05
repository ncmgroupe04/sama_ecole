using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Uniforms.Commands.ReturnStudentUniform;

/// <summary>
/// Reprend une tenue déjà remise (mauvaise taille, échange) : la ligne passe à « retournée » ET une entrée de stock
/// s'écrit au journal — le mouvement INVERSE de la remise, jamais une rature. Audité. Le motif est obligatoire.
/// </summary>
public record ReturnStudentUniformCommand : IRequest<StudentUniformDto>, IAuditableRequest
{
    public Guid Id { get; init; }

    public uint RowVersion { get; init; }

    public string? Reason { get; init; }
}
