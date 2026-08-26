using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Fiche de prêt/attribution d'un bien à un élève, un enseignant ou un membre du personnel —
/// le support de la « décharge » signée en début d'année pour les manuels scolaires, et du suivi du
/// matériel confié (PC, vidéoprojecteur).
///
/// Le bénéficiaire est désigné par TROIS clés étrangères nullables plutôt que par un identifiant
/// polymorphe générique : l'intégrité référentielle reste réelle, et la RLS couvre le bénéficiaire
/// comme le bien. Une contrainte CHECK impose qu'exactement une des trois soit renseignée, et qu'elle
/// corresponde à <see cref="BeneficiaryType"/>.
/// </summary>
public class ItemAssignment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid ItemId { get; set; }

    public InventoryItem? Item { get; set; }

    /// <summary>Douze manuels remis à un même élève tiennent en UNE fiche, pas douze.</summary>
    public int Quantity { get; set; }

    public AssignmentBeneficiaryType BeneficiaryType { get; set; }

    public Guid? StudentId { get; set; }

    public Guid? TeacherId { get; set; }

    /// <summary>Personnel administratif : l'utilisateur de la plateforme (<see cref="User"/>).</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Nom du bénéficiaire FIGÉ au moment de l'affectation. Même idiome que
    /// <see cref="EnrollmentFeeLine"/> vis-à-vis du barème : une décharge réimprimée trois ans plus
    /// tard doit être identique à celle qui a été signée, quoi qu'il soit advenu de la fiche de
    /// l'élève entre-temps.
    /// </summary>
    public required string BeneficiaryLabel { get; set; }

    public DateOnly AssignedOn { get; set; }

    /// <summary>Retour attendu — typiquement la fin de l'année scolaire pour les manuels. Facultatif.</summary>
    public DateOnly? DueOn { get; set; }

    public DateOnly? ReturnedOn { get; set; }

    public int? ReturnedQuantity { get; set; }

    /// <summary>État constaté au retour. <c>HorsService</c> réforme la part rendue au lieu de la remettre en circulation.</summary>
    public ItemCondition? ReturnCondition { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.EnCours;

    public string? Notes { get; set; }
}
