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
    Teacher
}
