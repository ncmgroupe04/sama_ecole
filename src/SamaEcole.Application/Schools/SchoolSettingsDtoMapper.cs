using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Schools;

/// <summary>
/// UNE SEULE conversion <see cref="SchoolSettings"/> → <see cref="SchoolSettingsDto"/>. Elle était recopiée à
/// l'identique dans GetSchoolSettingsQuery, UpdateSchoolSettingsCommandHandler et
/// ApplyEstablishmentProfileCommandHandler ; chaque nouveau réglage (comme les cycles gérés) devait être ajouté
/// à trois endroits, et l'oubli d'un seul laissait un champ vide dans la réponse d'un seul de ces endpoints.
/// </summary>
public static class SchoolSettingsDtoMapper
{
    public static SchoolSettingsDto From(SchoolSettings settings) => new(
        settings.GradingScale.ToString(),
        settings.StudentMatriculeFormat,
        settings.TeacherMatriculeFormat,
        settings.AutoLogoutMinutes,
        settings.DateFormat,
        settings.TuitionMonthsPerYear,
        settings.AllowSecretaryToManageGrading,
        settings.AllowFinanceToModifyFees,
        settings.AllowFinanceToDeleteFees,
        settings.DirectorSignatureUrl,
        settings.SecretarySignatureUrl,
        settings.CashierSignatureUrl,
        settings.OfficialStampUrl,
        settings.SurveillantSignatureUrl,
        settings.TypeEtablissement.ToString(),
        settings.SmsOnAttendanceAlert,
        settings.SmsOnDuesReminder,
        settings.SmsOnPaymentReceipt,
        settings.SmsCreditBalance,
        settings.DebtorReminderThresholdDays,
        settings.IsPedagogyEnabled,
        settings.IsFinanceEnabled,
        settings.IsInternatEnabled,
        settings.IsCoranModuleEnabled,
        settings.GradeEditWindowDays,
        settings.EvaluationPeriodType.ToString(),
        settings.CustomPeriodCount,
        SchoolWeek.ToNames(SchoolWeek.FromStored(settings.WorkingDays)),
        settings.ProfileEtablissement?.ToString(),
        ManagedCycles: ManagedCycleSet.ToNames(ManagedCycleSet.FromStored(settings.ManagedCycles)));
}
