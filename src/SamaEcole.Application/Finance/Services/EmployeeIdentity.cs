using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

/// <summary>
/// Nom et fonction de la personne liée à un contrat (Volume 1 §14.1 et §14.7) : un enseignant, un utilisateur
/// de la plateforme, ou un membre du personnel SANS compte saisi sur le contrat. Le contrat doit être chargé avec
/// <c>Teacher</c> et <c>User</c> ; pour une requête LINQ-to-SQL, répliquer l'expression telle quelle.
/// </summary>
public static class EmployeeIdentity
{
    public const string DefaultExternalRole = "Personnel";

    public static string FullName(EmployeeContract c) =>
        c.Teacher?.FullName ?? c.User?.FullName ?? c.ExternalFullName ?? "—";

    public static string Role(EmployeeContract c) =>
        c.Teacher is not null ? "Enseignant"
        : c.User is not null ? c.User.Role.ToString()
        : string.IsNullOrWhiteSpace(c.ExternalJobTitle) ? DefaultExternalRole : c.ExternalJobTitle!;
}
