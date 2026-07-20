using MediatR;

namespace SamaEcole.Application.Teachers.Commands.SetTeacherPhoto;

/// <summary>
/// PUT /api/v1/teachers/{id}/photo — feature B. Même contrat que
/// <see cref="Students.Commands.SetStudentPhoto.SetStudentPhotoCommand"/> : commande dédiée, séparée
/// d'UpdateTeacherCommand pour ne jamais perdre la photo à une modification non liée de la fiche.
/// </summary>
public record SetTeacherPhotoCommand(Guid TeacherId, string? PhotoDataBase64, uint RowVersion)
    : IRequest<SetTeacherPhotoResult>;

public record SetTeacherPhotoResult(Guid TeacherId, uint RowVersion, string? PhotoDisplayUrl);
