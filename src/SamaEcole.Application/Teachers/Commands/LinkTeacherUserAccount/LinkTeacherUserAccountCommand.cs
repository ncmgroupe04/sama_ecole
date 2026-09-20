using MediatR;

namespace SamaEcole.Application.Teachers.Commands.LinkTeacherUserAccount;

/// <summary>
/// PUT /api/v1/teachers/{id}/user-account — ticket JGK-D06bis. Rattache, change ou retire
/// (<c>UserId</c> null) le compte de connexion (rôle Enseignant) d'une fiche déjà créée.
///
/// Commande DÉDIÉE, distincte d'UpdateTeacherCommand : le rattachement d'un compte est une opération
/// SÉCURISÉE (elle change à qui appartient l'accès à la saisie de l'appel de CET enseignant), hors du
/// périmètre « correction de fiche administrative » — voir le commentaire d'UpdateTeacherCommand.
/// Réservée au Directeur seul (TeachersController), pas au Secrétariat qui crée les fiches :
/// docs/Volume_4_API_Design.md §19 renvoie déjà tout compte Enseignant non rattaché vers « le
/// rattachement au Directeur ».
///
/// Flux métier que ce ticket corrige : jusqu'ici, <c>UserId</c> ne pouvait être posé qu'À LA CRÉATION
/// de la fiche (CreateTeacherCommand), imposant de créer le compte AVANT la fiche enseignant. Le flux
/// réel de l'école est inverse — la fiche RH existe déjà, souvent de longue date, et le compte de
/// connexion est ouvert après coup, ou doit être changé (ex. une fiche créée avec le mauvais compte).
/// </summary>
public record LinkTeacherUserAccountCommand(Guid TeacherId, Guid? UserId, uint RowVersion)
    : IRequest<LinkTeacherUserAccountResult>;

public record LinkTeacherUserAccountResult(Guid TeacherId, Guid? UserId, uint RowVersion);
