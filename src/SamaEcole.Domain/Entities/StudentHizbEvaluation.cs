using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Journal des évaluations Hizb d'un élève : UNE ligne par saisie de l'Oustaz qui change quelque chose (quarts ou note). À
/// l'inverse de <see cref="StudentHizbStatus"/>, qui n'est que l'ÉTAT COURANT, ce journal conserve l'histoire — c'est lui qui
/// permet de tracer une courbe d'évolution.
///
/// APPEND-ONLY : le rôle applicatif n'a ni UPDATE ni DELETE sur la table (voir la migration). Une saisie fausse se corrige par
/// une nouvelle saisie, jamais en réécrivant le passé. Pas de suppression logique non plus : une évaluation n'est pas « annulable ».
///
/// <see cref="TotalQuartersAfter"/> est le total de l'élève (0 à 240) APRÈS cette saisie : la courbe se lit directement dans le
/// journal, sans recalculer l'état de chacun des 60 Hizb à chaque date.
///
/// REPRISE : à la création de la table, un état courant existant donne une ligne <see cref="IsBaseline"/> (point de départ). Le
/// passé antérieur n'est pas reconstituable : l'historique commence au déploiement.
/// </summary>
public class StudentHizbEvaluation : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Numéro du Hizb évalué, de 1 à 60.</summary>
    public int HizbNumber { get; set; }

    /// <summary>Quarts acquis sur ce Hizb AVANT la saisie (0 à 4).</summary>
    public int QuartersBefore { get; set; }

    /// <summary>Quarts acquis sur ce Hizb APRÈS la saisie (0 à 4).</summary>
    public int QuartersAfter { get; set; }

    /// <summary>Note de récitation de 1 à 5 posée par la saisie ; nulle quand le Hizb revient à 0 quart.</summary>
    public int? Rating { get; set; }

    /// <summary>Total de quarts de l'élève après la saisie, tous Hizb confondus (0 à 240).</summary>
    public int TotalQuartersAfter { get; set; }

    /// <summary>Date de la saisie, décidée par le SERVEUR (jamais envoyée par le client).</summary>
    public DateTimeOffset EvaluatedAt { get; set; }

    /// <summary>Compte qui a saisi (Oustaz ou Direction). Sans clé étrangère : le journal survit à la vie du compte.</summary>
    public Guid? EvaluatedByUserId { get; set; }

    /// <summary>Oustaz de l'élève AU MOMENT de la saisie (il peut changer de Halqa ensuite).</summary>
    public Guid? InstructorId { get; set; }

    /// <summary>Ligne de départ créée par la migration à partir d'un état courant déjà saisi.</summary>
    public bool IsBaseline { get; set; }
}
