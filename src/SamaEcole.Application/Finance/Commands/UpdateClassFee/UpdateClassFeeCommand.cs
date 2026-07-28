using MediatR;

namespace SamaEcole.Application.Finance.Commands.UpdateClassFee;

/// <summary>
/// PUT /api/v1/finance/fees/{id} — ticket JGK-F01, « Option 2 : exception par classe ».
///
/// Ajuste le montant d'UNE ligne de barème. <paramref name="RowVersion"/> est le jeton de
/// concurrence (xmin) lu à l'affichage : si un autre utilisateur a modifié cette ligne entre-temps,
/// l'écriture est refusée en 409 plutôt que d'écraser en silence (AGENTS.md règle #5). Le changement
/// est historisé (ancien → nouveau).
/// </summary>
public record UpdateClassFeeCommand(Guid Id, decimal Amount, uint RowVersion) : IRequest<UpdateClassFeeResult>;

public record UpdateClassFeeResult(Guid Id, decimal Amount, uint RowVersion);
