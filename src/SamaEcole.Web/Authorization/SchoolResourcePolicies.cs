namespace SamaEcole.Web.Authorization;

/// <summary>Nom de la policy resource-based garantissant que la ressource ciblée appartient bien à l'école du JWT appelant.</summary>
public static class SchoolResourcePolicies
{
    /// <summary>
    /// À invoquer via <see cref="Microsoft.AspNetCore.Authorization.IAuthorizationService"/> avec, en
    /// resource, le <c>schoolId</c> lu dans le segment de route (ex. <c>AuthorizeAsync(User, schoolId,
    /// SchoolResourcePolicies.CanAccessSchoolResource)</c>) — impossible à exprimer en attribut
    /// déclaratif puisque la resource n'est connue qu'à l'exécution de l'action. Voir
    /// SchoolResourceAuthorizationHandler pour la règle évaluée.
    /// </summary>
    public const string CanAccessSchoolResource = "CanAccessSchoolResource";
}
