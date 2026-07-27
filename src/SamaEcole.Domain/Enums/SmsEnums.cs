namespace SamaEcole.Domain.Enums;

/// <summary>
/// Événement à l'origine d'un SMS. Chaque valeur automatique correspond à un commutateur
/// d'activation dans les paramètres de l'école (SchoolSettings) : une école peut vouloir les
/// alertes d'assiduité sans les relances d'impayés, ou l'inverse.
/// </summary>
public enum SmsTrigger
{
    /// <summary>Retard ou absence saisi(e) — alerte au parent/tuteur.</summary>
    AttendanceAlert,

    /// <summary>Relance d'impayé / rappel de frais de scolarité.</summary>
    DuesReminder,

    /// <summary>Confirmation d'un encaissement, avec le lien vers le reçu.</summary>
    PaymentReceipt,

    /// <summary>
    /// Mise à disposition du bulletin de notes. Le PDF lui-même part par WhatsApp ou e-mail : un SMS
    /// ne transporte pas de pièce jointe, il annonce seulement au tuteur que le bulletin est prêt.
    /// </summary>
    ReportCard,

    /// <summary>Envoi ponctuel décidé par un utilisateur, hors automatisme.</summary>
    Manual
}

/// <summary>
/// Cycle de vie d'un SMS. Les trois premiers statuts sont ceux d'une FILE D'ATTENTE : le déclencheur
/// métier ne fait qu'inscrire le message en <see cref="Pending"/> et rend la main ; c'est
/// SmsQueueProcessor qui le remet au fournisseur, hors de la requête HTTP.
///
///   Pending ──(worker)──▶ Sent ──(accusé de réception)──▶ Delivered
///      │                   │
///      │                   └────(accusé négatif)────────▶ Failed
///      └──(tentatives épuisées)───────────────────────────▶ Failed
///
/// Stocké en TEXTE (HasConversion&lt;string&gt;) : l'ordre de déclaration ne porte donc aucune
/// sémantique, et ajouter une valeur ne réécrit pas les lignes existantes.
/// </summary>
public enum SmsDeliveryStatus
{
    /// <summary>
    /// En file, pas encore remis au fournisseur. Le solde est DÉJÀ débité (voir SmsDispatcher) :
    /// c'est ce qui empêche une relance de masse de promettre plus de segments qu'il n'en reste.
    /// </summary>
    Pending,

    /// <summary>Accepté par le fournisseur. La remise effective au téléphone n'est pas garantie pour autant.</summary>
    Sent,

    /// <summary>
    /// Remise au téléphone CONFIRMÉE par l'accusé de réception (DLR) de l'agrégateur. Seul statut qui
    /// atteste qu'un parent a réellement reçu l'alerte — <see cref="Sent"/> n'atteste que de l'envoi.
    /// </summary>
    Delivered,

    /// <summary>Refusé par le fournisseur (numéro invalide, quota, panne), tentatives épuisées.</summary>
    Failed,

    /// <summary>Non tenté : le solde SMS de l'école était épuisé. Journalisé pour expliquer le silence.</summary>
    InsufficientCredit
}
