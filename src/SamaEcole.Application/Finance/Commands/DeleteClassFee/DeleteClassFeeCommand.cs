using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Finance.Commands.DeleteClassFee;

/// <summary>
/// DELETE /api/v1/finance/fees/{id} — supprime UNE ligne de barème (matrice d'autorisation
/// "Photoshop", volet Finance).
///
/// Toujours un SOFT DELETE (AGENTS.md règle #6), y compris quand aucun paiement n'y est lié :
/// EnrollmentFeeLine fige déjà un instantané du montant à l'inscription (voir son commentaire de
/// classe), donc rien ne dépend structurellement de cette ligne de barème pour l'historique
/// comptable — la suppression peut donc être libre, sans jamais recourir à un DELETE SQL réel. La
/// trace de la suppression elle-même vit sur DeletedAt/DeletedBy (AuditableEntity), pas dans
/// FeeChangeHistory (dédiée aux changements de MONTANT, volontairement append-only).
///
/// <paramref name="RowVersion"/> : même verrouillage optimiste que UpdateClassFeeCommand (AGENTS.md
/// règle #5) — supprimer une ligne modifiée entre-temps par un autre utilisateur échoue en 409,
/// jamais un écrasement silencieux.
/// </summary>
public record DeleteClassFeeCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;
