namespace SamaEcole.Domain.Enums;

public enum EntityStatus
{
    Active,
    Suspended,
    Blocked
}

public enum EnrollmentType
{
    NewEnrollment,
    ReEnrollment
}

public enum EnrollmentStatus
{
    Pending,
    Confirmed,
    Cancelled
}

public enum PaymentMethod
{
    Cash,
    MobileMoney,
    BankTransfer
}

public enum PaymentCategory
{
    Enrollment,
    Tuition,
    Exam,
    Other
}

public enum SubscriptionPlan
{
    Primaire,
    Standard,
    Premium
}

public enum SubscriptionStatus
{
    Active,
    Suspended,
    ReadOnly
}

public enum MatriculeKind
{
    Student,
    Teacher,

    /// <summary>
    /// Numéro de reçu d'inscription (ticket JGK-E02, ex. « REC-2025-0002 »). Réutilise le compteur
    /// séquentiel par (école, type, année) des matricules : même garantie de numérotation officielle
    /// unique et sans trou, incrémentée dans la transaction d'inscription.
    /// </summary>
    Receipt
}
