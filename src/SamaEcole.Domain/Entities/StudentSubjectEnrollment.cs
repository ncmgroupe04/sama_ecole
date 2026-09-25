using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Choix d'option d'un élève pour UNE année scolaire (Évolution N°6) : l'élève suit la matière optionnelle
/// <see cref="ClassSubjectId"/> de sa classe (sa LV2, son option scientifique…). Une ligne par option retenue ;
/// n'existe que pour une matière d'un groupe d'options.
///
/// Rattaché à l'année, comme les surcharges de coefficient : le choix de 2027 ne réécrit pas le bulletin de
/// 2026. Un changement d'option se fait par suppression logique de l'ancien choix et création du nouveau
/// (règle #6), jamais par une modification en place.
/// </summary>
public class StudentSubjectEnrollment : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public Guid ClassSubjectId { get; set; }

    public Guid SchoolYearId { get; set; }
}
