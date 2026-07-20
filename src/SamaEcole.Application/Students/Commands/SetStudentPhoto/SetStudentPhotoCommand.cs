using MediatR;

namespace SamaEcole.Application.Students.Commands.SetStudentPhoto;

/// <summary>
/// PUT /api/v1/students/{id}/photo — feature B (upload local de la photo). Commande DÉDIÉE, séparée
/// d'UpdateStudentCommand : mélanger la photo dans la mise à jour générale de la fiche ferait perdre la
/// photo silencieusement à la première modification de nom/classe/tuteur qui omettrait de la
/// retransmettre. Ici, l'appel n'est déclenché QUE par une interaction explicite sur le composant photo
/// (dépôt d'un fichier ou clic sur « Retirer la photo »).
///
/// <see cref="PhotoDataBase64"/> est déjà compressé CÔTÉ CLIENT (Canvas 300×300, JPEG qualité 80 % —
/// voir Student.PhotoData) ; le serveur ne fait que décoder et borner la taille (validateur). Null =
/// retire la photo téléversée — l'élève retombe alors sur son PhotoUrl externe s'il en a un, sinon sur
/// les initiales (voir PhotoDisplay.ToDisplayUrl) — jamais une suppression d'une autre donnée.
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation (verrouillage optimiste,
/// AGENTS.md règle #5), même contrat qu'UpdateStudentCommand.
/// </summary>
public record SetStudentPhotoCommand(Guid StudentId, string? PhotoDataBase64, uint RowVersion)
    : IRequest<SetStudentPhotoResult>;

public record SetStudentPhotoResult(Guid StudentId, uint RowVersion, string? PhotoDisplayUrl);
