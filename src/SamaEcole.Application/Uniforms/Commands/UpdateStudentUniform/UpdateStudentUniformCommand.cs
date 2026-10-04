using MediatR;

namespace SamaEcole.Application.Uniforms.Commands.UpdateStudentUniform;

/// <summary>
/// Change la taille ou la quantité d'une ligne encore à remettre. <c>UniformVariantId</c> <c>null</c> remet la taille
/// « à confirmer ». <c>RowVersion</c> : le jeton xmin lu avec la ligne (409 s'il est périmé).
/// </summary>
public record UpdateStudentUniformCommand : IRequest<StudentUniformDto>
{
    public Guid Id { get; init; }

    public Guid? UniformVariantId { get; init; }

    public int Quantity { get; init; } = 1;

    public uint RowVersion { get; init; }
}
