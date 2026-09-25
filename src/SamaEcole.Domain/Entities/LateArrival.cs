using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

public class LateArrival : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = null!;
    
    public DateTime Date { get; set; }
    public int Minutes { get; set; }
    public string Reason { get; set; } = null!;
    public string? Observations { get; set; }

    // ---- Circuit du billet d'entrée (Évolution N°5) --------------------------------------------------
    // Toutes ces colonnes sont NULLABLES : un billet sans cours visé (Status null) se comporte exactement
    // comme avant, y compris pour tout l'historique existant.

    /// <summary>Cours d'emploi du temps que l'élève rejoint. Null = billet sans cours visé.</summary>
    public Guid? TargetScheduleSlotId { get; set; }

    /// <summary>Null tant qu'aucun cours n'est visé ; sinon Issued, puis Accepted ou Cancelled.</summary>
    public EntryTicketStatus? Status { get; set; }

    /// <summary>Compte de l'enseignant (ou du Directeur) qui a accepté — jamais lu du corps de requête.</summary>
    public Guid? AcceptedByUserId { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid? CancelledByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>
    /// Statut et minutes de la ligne d'appel AVANT que ce billet ne la passe en retard : ce qui permet de
    /// la restaurer si le billet est annulé avant acceptation. Null si la fiche n'existait pas encore.
    /// </summary>
    public AttendanceStatus? PreviousStatus { get; set; }
    public int? PreviousLateMinutes { get; set; }

    // ---- Billet par heure d'arrivée (Complément N°5 bis) ---------------------------------------------------
    // Nullables : un billet saisi à l'ancienne (minutes à la main) les laisse vides et se comporte comme avant.

    /// <summary>Heure d'arrivée réelle de l'élève, saisie par la Vie Scolaire. Le reste en est DÉDUIT à l'émission.</summary>
    public TimeOnly? ArrivalTime { get; set; }

    /// <summary>
    /// Durée totale régularisée (cours manqués + retard), calculée à l'ÉMISSION et conservée : modifier l'emploi du
    /// temps plus tard ne réécrit pas un billet déjà imprimé.
    /// </summary>
    public int? TotalMinutes { get; set; }

    /// <summary>
    /// Cours entièrement manqués avant l'arrivée (instantané, même raison). Le retard éventuel, lui, porte sur
    /// <see cref="TargetScheduleSlotId"/> ; <see cref="Minutes"/> vaut alors les seules minutes de retard (0 possible).
    /// </summary>
    public Guid[]? MissedScheduleSlotIds { get; set; }
}
