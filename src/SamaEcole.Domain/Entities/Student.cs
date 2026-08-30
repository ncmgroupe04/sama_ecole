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

    /// <summary>
    /// Identifiant National de l'Élève (IEN) — le numéro qui suit l'élève d'un établissement à l'autre
    /// sur tout son parcours, et la clé de rapprochement de TOUS les échanges avec le ministère
    /// (Planète, STATEDUC, dossiers d'examen). Voir Volume 1 §23.1.
    ///
    /// NULLABLE, et ce n'est pas une commodité : l'IEN est attribué par l'administration centrale, pas
    /// par l'école. Un élève fraîchement inscrit n'en a légitimement aucun tant que le SIMEN ne l'a pas
    /// délivré. Le rendre obligatoire bloquerait l'inscription — exactement ce que le terrain ne peut
    /// pas se permettre à la rentrée. L'export Planète imprime alors une cellule vide, jamais un numéro
    /// inventé, et le rapport STATEDUC compte ces élèves dans une ligne « sans IEN » explicite.
    ///
    /// Distinct de <see cref="Matricule"/>, qui est INTERNE à l'établissement et n'a aucune valeur hors
    /// de lui : deux écoles peuvent porter le même matricule pour deux élèves différents, jamais le
    /// même IEN.
    ///
    /// Unicité : index UNIQUE PARTIEL <c>(SchoolId, IenNumber) WHERE "IenNumber" IS NOT NULL</c> — le
    /// partiel est indispensable, sans quoi deux élèves sans IEN (deux NULL) seraient... acceptés par
    /// un index UNIQUE standard mais refusés dès qu'on y ajoute un jour une contrainte plus stricte.
    /// L'unicité est bornée à l'école faute de pouvoir vérifier l'unicité nationale sans le SIMEN.
    ///
    /// Ce champ n'est JAMAIS écrit par <c>CreateStudentCommand</c> : il se renseigne par
    /// <c>AssignStudentIenCommand</c> (saisie d'un IEN officiel reçu) ou par génération algorithmique
    /// de secours (<c>IIenGeneratorService</c>) — voir la mise en garde de cette interface.
    /// </summary>
    public string? IenNumber { get; set; }

    /// <summary>
    /// Vrai quand <see cref="IenNumber"/> a été produit par l'algorithme de SECOURS
    /// (<c>NationalIenGenerator</c>) et non reçu du ministère. Un IEN provisoire n'a AUCUNE valeur
    /// officielle : il sert à ne pas bloquer les traitements internes en attendant le vrai numéro.
    ///
    /// C'est ce drapeau qui permet à l'export Planète de marquer la ligne comme provisoire plutôt que
    /// de la présenter à l'IEF comme un identifiant national valide — présenter un numéro fabriqué
    /// comme officiel serait un faux. Il est remis à <c>false</c> le jour où l'IEN officiel écrase le
    /// provisoire (<c>AssignStudentIenCommand</c>).
    /// </summary>
    public bool IsIenProvisional { get; set; }

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
