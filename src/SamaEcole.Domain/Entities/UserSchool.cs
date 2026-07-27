using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Rattachement d'un utilisateur à un établissement SUPPLÉMENTAIRE (groupe scolaire) : un promoteur
/// qui possède plusieurs écoles bascule de l'une à l'autre sans se déconnecter.
///
/// Table PLATEFORME, hors RLS — et c'est structurel, pas une facilité : les lignes utiles à la
/// bascule sont précisément celles des AUTRES écoles que celle de la session courante. Une policy
/// sur SchoolId masquerait donc exactement ce qu'on vient y chercher. Le filtrage se fait sur
/// <see cref="UserId"/>, valeur issue du claim `sub` du JWT, jamais d'un paramètre client.
///
/// ADDITIF PAR CONSTRUCTION : un utilisateur sans aucune ligne ici garde le comportement d'origine
/// — son unique école reste <c>User.SchoolId</c>, et aucun sélecteur ne s'affiche.
/// </summary>
public class UserSchool : AuditableEntity
{
    public Guid UserId { get; set; }
    public Guid SchoolId { get; set; }
}
