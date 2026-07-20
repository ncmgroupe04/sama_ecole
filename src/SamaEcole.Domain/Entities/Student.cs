using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Entité de référence à suivre pour tout nouveau module tenant : voir AGENTS.md règle #2 et #3.
/// Le matricule est renseigné uniquement par le Handler de création, dans la même transaction
/// que l'insertion (jamais pré-généré à l'ouverture d'un formulaire) — voir ticket JGK-D01.
/// </summary>
public class Student : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Matricule { get; set; }
    public required string FullName { get; set; }
    public DateOnly BirthDate { get; set; }

    /// <summary>
    /// Lieu de naissance — OBLIGATOIRE au Sénégal (exigence juridique et académique, mention imprimée
    /// sur le bulletin « Né(e) le … à … »). Non-nullable en base (colonne NOT NULL, migration
    /// MakeStudentBirthPlaceRequired) ET exigé à la saisie par les validateurs de création/inscription :
    /// un élève sans lieu de naissance n'est pas un élève enregistrable. Contraste voulu avec
    /// <see cref="Teacher.BirthPlace"/>, resté optionnel : un enseignant n'a pas de bulletin.
    /// </summary>
    public required string BirthPlace { get; set; }

    public required string Gender { get; set; } // "M" | "F"
    public Guid ClassroomId { get; set; }

    /// <summary>URL de la photo d'identité (docs/Volume_3_DDS.md : PhotoUrl). Même contrat qu'un LogoUrl
    /// d'école : une adresse http(s) saisie par l'utilisateur, jamais un fichier téléversé.</summary>
    public string? PhotoUrl { get; set; }

    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
}
