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

    /// <summary>Envoi ponctuel décidé par un utilisateur, hors automatisme.</summary>
    Manual
}

public enum SmsDeliveryStatus
{
    /// <summary>Accepté par le fournisseur. La remise effective au téléphone n'est pas garantie pour autant.</summary>
    Sent,

    /// <summary>Refusé par le fournisseur (numéro invalide, quota, panne).</summary>
    Failed,

    /// <summary>Non tenté : le solde SMS de l'école était épuisé. Journalisé pour expliquer le silence.</summary>
    InsufficientCredit
}
