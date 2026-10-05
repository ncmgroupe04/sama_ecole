using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Uniforms.Commands.DeliverStudentUniform;

/// <summary>
/// Remet la tenue à l'élève : la ligne passe à « remise » ET une sortie de stock s'écrit au journal, dans UNE
/// transaction. <see cref="IAuditableRequest"/> : une remise fait sortir du stock, elle est toujours historisée.
///
/// La date et l'auteur ne viennent jamais du client (horloge et JWT du serveur). <c>RowVersion</c> est le jeton xmin de
/// la LIGNE ; celui du lot de stock est lu par le serveur au moment de la remise, sous verrou.
/// </summary>
public record DeliverStudentUniformCommand : IRequest<StudentUniformDto>, IAuditableRequest
{
    public Guid Id { get; init; }

    public uint RowVersion { get; init; }

    /// <summary>Motif d'une remise non facturée (obligatoire dans ce cas, écarté sinon).</summary>
    public string? NotBilledReason { get; init; }
}
