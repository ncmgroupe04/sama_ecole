using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Trimestre d'une année scolaire (ticket JGK-G01). Généré automatiquement — trois occurrences, dates
/// réparties sur la période — dès qu'une <see cref="SchoolYear"/> est créée (voir
/// CreateSchoolYearCommandHandler) : aucun écran ne les crée, faute de ticket dédié dans le backlog, et
/// le système sénégalais standard compte trois trimestres par année.
///
/// <see cref="Order"/> (1, 2, 3) porte le classement affiché sur le bulletin (Volume 1 §8.1) — le
/// libellé, lui, reste un texte d'affichage, jamais une clé de tri.
/// </summary>
public class Term : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid SchoolYearId { get; set; }

    /// <summary>Ex. « 1er trimestre ».</summary>
    public required string Label { get; set; }

    public int Order { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }
}
