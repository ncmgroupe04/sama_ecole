using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Convocation d'un parent/tuteur à un entretien avec l'établissement (module Vie Scolaire) —
/// motif libre (discipline, assiduité, résultats…), pas restreinte à la discipline pure : c'est un
/// entretien programmé, distinct d'un <see cref="DisciplineRecord"/> qui, lui, sanctionne un fait déjà
/// constaté.
/// </summary>
public class ParentSummons : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public DateTimeOffset ScheduledAt { get; set; }
    public string Reason { get; set; } = null!;

    // ------------------------------------------------------------------ Suite donnée (02/09/2026)
    //
    // Sans ces quatre colonnes, une convocation restait éternellement ouverte : rien ne disait si le
    // parent était venu. Le registre listait des rendez-vous, jamais des entretiens — inexploitable
    // comme pièce de vie scolaire, et impossible à produire devant l'inspection.
    //
    // La suite se pose UNE FOIS, depuis Scheduled (CloseParentSummonsCommandHandler). On ne repasse
    // jamais en Scheduled ni d'une suite à une autre : même intégrité que le registre disciplinaire
    // (Volume 1 §18.1). Une erreur se corrige en émettant une nouvelle convocation.

    public ParentSummonsStatus Status { get; set; } = ParentSummonsStatus.Scheduled;

    /// <summary>Compte rendu de l'entretien, ou motif de l'absence / du report.</summary>
    public string? OutcomeNotes { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Qui a consigné la suite. Même idiome que <see cref="DebtorReminderBatch.SentByUserId"/>.</summary>
    public Guid? ClosedByUserId { get; set; }
}
