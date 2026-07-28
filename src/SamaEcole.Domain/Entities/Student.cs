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

    /// <summary>
    /// URL de la photo d'identité (docs/Volume_3_DDS.md : PhotoUrl) — une adresse http(s) EXTERNE saisie
    /// par l'utilisateur. Reste une alternative valide à <see cref="PhotoData"/> (feature B) : une école
    /// qui héberge déjà ses photos ailleurs n'est pas obligée de téléverser. Priorité d'affichage à la
    /// lecture (voir PhotoDisplay.ToDisplayUrl) : PhotoData l'emporte si présent, PhotoUrl sinon.
    /// </summary>
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Photo d'identité TÉLÉVERSÉE (feature B), déjà compressée CÔTÉ CLIENT (Canvas 300×300, JPEG
    /// qualité 80 % — ~30 Ko) avant l'envoi : le serveur ne redimensionne ni ne recompresse, il valide
    /// seulement une borne de taille (PhotoValidation.MaxPhotoBytes). Toujours du JPEG — pas de colonne
    /// de content-type séparée, le format est fixé par le pipeline de compression client, pas par
    /// l'utilisateur. Distincte de <see cref="PhotoUrl"/> : gérée par une commande dédiée
    /// (SetStudentPhotoCommand), jamais par UpdateStudentCommand — mélanger les deux ferait perdre la
    /// photo silencieusement à la moindre modification de fiche qui omettrait de la retransmettre.
    /// </summary>
    public byte[]? PhotoData { get; set; }

    public string? GuardianName { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianEmail { get; set; }

    /// <summary>Adresse du domicile de l'élève (facultative, distincte des coordonnées du tuteur).</summary>
    public string? Address { get; set; }
}
