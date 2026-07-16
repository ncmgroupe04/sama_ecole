using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Note d'un élève, dans une matière, pour un trimestre et un type d'évaluation donnés (ticket
/// JGK-G01, docs/ERD.md). Une ligne par (StudentId, SubjectId, TermId, EvaluationType) : le bulletin
/// (JGK-G03) exige un Devoir ET une Composition distincts avant la moyenne pondérée par matière.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : deux enseignants qui corrigent la même note en même temps
/// ne doivent jamais s'écraser en silence. Le jeton est la colonne système xmin de PostgreSQL,
/// configurée dans GradeConfiguration — comme ClassFee et Enrollment.
/// </summary>
public class Grade : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    public Guid SubjectId { get; set; }

    public Guid TermId { get; set; }

    public EvaluationType EvaluationType { get; set; }

    /// <summary>Sur le barème de l'école (SchoolSettings.GradingScale : 10 ou 20), jamais négative.</summary>
    public decimal Value { get; set; }
}
