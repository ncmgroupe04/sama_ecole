using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Billet d'absence : justifie une absence de plusieurs jours (maladie, deuil, voyage…) pour UN élève, de
/// <see cref="StartDate"/> à <see cref="EndDate"/> inclus. Tant qu'il est actif, l'appel de ces jours présélectionne
/// « Absent (justifié) » ; l'annuler (soft delete) rend aux lignes d'appel leur statut d'avant.
/// </summary>
public class AbsenceJustification : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    /// <summary>Premier jour d'absence couvert (inclus).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Dernier jour d'absence couvert (inclus) ; égal à <see cref="StartDate"/> pour une absence d'un jour.</summary>
    public DateOnly EndDate { get; set; }

    public string Reason { get; set; } = null!;
    public DateOnly? AuthorizedReturnDate { get; set; }

    public bool Covers(DateOnly day) => day >= StartDate && day <= EndDate;
}
