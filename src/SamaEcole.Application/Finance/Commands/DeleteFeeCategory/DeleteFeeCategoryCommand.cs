using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Finance.Commands.DeleteFeeCategory;

/// <summary>
/// DELETE /api/v1/finance/fee-categories/{id} — supprime (soft delete) une catégorie de frais ET,
/// en cascade dans la MÊME transaction, toutes ses lignes de barème (ClassFee) associées : une
/// catégorie disparue ne doit jamais laisser de lignes orphelines encore visibles dans l'interface.
///
/// Pas de RowVersion ici, contrairement à DeleteClassFeeCommand : FeeCategory ne porte aujourd'hui
/// aucun verrou optimiste (aucun PUT n'existe sur elle — Name/IsRecurring sont figés à la création),
/// donc rien à protéger d'une modification concurrente. Un double DELETE reste sans risque : le
/// Global Query Filter rend la catégorie déjà supprimée invisible, la seconde tentative reçoit 404.
///
/// La cascade ne vérifie PAS individuellement le xmin de chaque ClassFee enfant : c'est une
/// suppression groupée assumée par celui qui la déclenche — bloquer sur le conflit isolé d'une seule
/// ligne enfant serait une friction disproportionnée pour un geste par nature global.
/// </summary>
public record DeleteFeeCategoryCommand(Guid Id) : IRequest<Unit>, IAuditableRequest;
