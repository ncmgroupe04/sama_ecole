using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Oustaz (maître coranique) d'un daara : le responsable d'une Halqa, son cercle d'étude. Les élèves
/// qui lui sont rattachés le sont par <see cref="Student.InstructorId"/>.
///
/// Distinct de <see cref="Teacher"/> à dessein : un Oustaz n'a ni matricule RH, ni contrat, ni matière,
/// ni affectation de classe — ce n'est pas un enseignant du cursus. Le fusionner dans <c>teachers</c>
/// ferait entrer des Oustaz dans la paie, le STATEDUC et l'emploi du temps, où ils n'ont pas leur place.
/// </summary>
public class Instructor : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string FullName { get; set; }

    /// <summary>Nom en arabe, saisi librement — même principe que <see cref="Student.FullNameAr"/>, jamais traduit automatiquement.</summary>
    public string? FullNameAr { get; set; }

    public string? Phone { get; set; }

    public EntityStatus Status { get; set; } = EntityStatus.Active;

    /// <summary>
    /// Compte de connexion (rôle Enseignant) de l'Oustaz, pour la tablette — même principe que
    /// <see cref="Teacher.UserId"/>. Nullable : un Oustaz peut avoir une fiche sans jamais se connecter.
    /// C'est ce lien qui permet à l'API de borner l'écriture d'un Oustaz aux élèves de SA Halqa
    /// (<see cref="Student.InstructorId"/>) : le JWT ne porte que l'identifiant du compte, pas la fiche.
    /// Un compte ne peut être rattaché qu'à UN Oustaz (index unique partiel).
    /// </summary>
    public Guid? UserId { get; set; }
}
