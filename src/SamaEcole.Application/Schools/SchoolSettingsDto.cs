namespace SamaEcole.Application.Schools;

/// <summary>
/// openapi.yaml §SchoolSettings. `gradingScale` y est déclaré en CHAÎNE (« 10 » / « 20 ») alors que
/// le domaine le manipule en entier — le barème sert à calculer des moyennes, pas à être affiché.
/// La conversion est faite ici, une fois, plutôt que d'imposer un int.Parse à chaque module de notes.
/// </summary>
public record SchoolSettingsDto(
    string GradingScale,
    string StudentMatriculeFormat,
    string TeacherMatriculeFormat,
    int AutoLogoutMinutes,
    string DateFormat,
    int TuitionMonthsPerYear,
    bool AllowSecretaryToManageGrading,
    bool AllowFinanceToModifyFees,
    bool AllowFinanceToDeleteFees,
    string? DirectorSignatureUrl = null,
    string? SecretarySignatureUrl = null,
    string? CashierSignatureUrl = null,
    string? OfficialStampUrl = null,
    string? SurveillantSignatureUrl = null,
    string TypeEtablissement = "Prive",
    bool SmsOnAttendanceAlert = false,
    bool SmsOnDuesReminder = false,
    bool SmsOnPaymentReceipt = false,

    /// <summary>
    /// EN LECTURE SEULE — présent dans le DTO (l'écran de paramétrage affiche le solde à côté des
    /// commutateurs) mais ABSENT d'UpdateSchoolSettingsCommand : les crédits s'achètent, un Directeur
    /// qui pourrait les fixer lui-même s'offrirait des SMS. Seul TopUpSmsCreditsCommand (Super Admin)
    /// modifie cette valeur.
    /// </summary>
    int SmsCreditBalance = 0,

    /// <summary>Jours de retard avant qu'un débiteur n'entre dans un lot de relance brouillon (Étape 5).</summary>
    int DebtorReminderThresholdDays = 7,

    // Modules activés/désactivés par le Directeur, indépendamment de la formule d'abonnement (voir
    // SchoolModule) — Pédagogie et Finance sont le socle métier, actifs par défaut ; Internat et
    // Coran n'ont encore aucun module derrière eux (réglage anticipé), inactifs par défaut.
    bool IsPedagogyEnabled = true,
    bool IsFinanceEnabled = true,
    bool IsInternatEnabled = false,
    bool IsCoranModuleEnabled = false,

    /// <summary>Fenêtre de correction des notes par l'Enseignant, en jours (Directeur/Secrétariat : illimitée).</summary>
    int GradeEditWindowDays = 7);
