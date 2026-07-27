using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Historique des SMS envoyés par un établissement (offre Premium). Table tenant à part entière
/// (SchoolId + RLS + Global Query Filter) : un Directeur ne doit voir que les envois de SON école.
///
/// Une ligne est écrite pour CHAQUE tentative, y compris les échecs et les envois refusés faute de
/// crédit : c'est le journal qui justifie la consommation du solde auprès de l'école, il ne doit
/// donc pas se limiter aux succès.
/// </summary>
public class SmsMessage : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    /// <summary>Numéro destinataire au format international (+221…), tel qu'envoyé au fournisseur.</summary>
    public string Recipient { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Ce qui a provoqué l'envoi — permet de filtrer l'historique et d'imputer la consommation.</summary>
    public SmsTrigger Trigger { get; set; }

    public SmsDeliveryStatus Status { get; set; }

    /// <summary>Référence du fournisseur (Infobip/Orange/Twilio), pour rapprocher une facture ou un litige.</summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>Motif d'échec, repris tel quel dans l'historique côté interface. Null si l'envoi a réussi.</summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Nombre de segments facturés (un SMS au-delà de 160 caractères en consomme plusieurs). C'est
    /// CETTE valeur qui est débitée du solde, pas « 1 par message » — sans quoi le solde affiché
    /// divergerait de la facture du fournisseur.
    /// </summary>
    public int SegmentCount { get; set; } = 1;

    /// <summary>Élève concerné, quand l'envoi en découle (assiduité, impayé, reçu). Null pour un envoi manuel.</summary>
    public Guid? StudentId { get; set; }

    public DateTimeOffset SentAt { get; set; }
}
