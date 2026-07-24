namespace SamaEcole.Domain.Enums;

/// <summary>Voir docs/Volume_7_Security.md pour la matrice complète des permissions par rôle.</summary>
public enum Role
{
    SuperAdmin,
    Directeur,
    Secretariat,
    Finance,
    Enseignant,
    Surveillant
}
