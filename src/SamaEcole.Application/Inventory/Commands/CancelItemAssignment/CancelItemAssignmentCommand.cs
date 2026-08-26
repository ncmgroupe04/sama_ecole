using MediatR;

namespace SamaEcole.Application.Inventory.Commands.CancelItemAssignment;

/// <summary>
/// DELETE /api/v1/inventory/assignments/{id} — annule une fiche de prêt SAISIE PAR ERREUR (mauvais
/// bien, mauvais bénéficiaire), et rend au disponible les unités qu'elle avait retirées.
///
/// Ce n'est pas une restitution : rien n'a été rendu, la remise n'a jamais eu lieu. D'où le mouvement
/// de contre-passation dédié, dont le motif dit explicitement « annulation » — et non un faux retour
/// qui laisserait croire, à la lecture du journal, qu'un bien est passé entre les mains d'un élève.
/// </summary>
public record CancelItemAssignmentCommand(Guid Id, uint RowVersion) : IRequest<Unit>;
