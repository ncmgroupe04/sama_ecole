using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Schools.Queries.GetSchoolSettings;

/// <summary>
/// GET /schools/current/settings — ticket JGK-B02.
/// « current » vient du JWT, JAMAIS d'un paramètre client (AGENTS.md règle #10) : c'est ce qui
/// interdit à un Directeur de lire les réglages d'un autre établissement.
/// </summary>
public record GetSchoolSettingsQuery : IRequest<SchoolSettingsDto>;

public class GetSchoolSettingsQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetSchoolSettingsQuery, SchoolSettingsDto>
{
    public async Task<SchoolSettingsDto> Handle(GetSchoolSettingsQuery request, CancellationToken cancellationToken)
    {
        _ = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter cantonne déjà la lecture à l'école du JWT : il ne peut donc y avoir
        // qu'une ligne — celle de l'appelant.
        var settings = await dbContext.SchoolSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        // Aucun réglage stocké (école créée avant JGK-B02) : on renvoie les valeurs par défaut plutôt
        // qu'un 404. Une école a TOUJOURS des réglages, même si personne ne les a encore ouverts.
        return settings is null
            ? Defaults()
            : Map(settings);
    }

    private static SchoolSettingsDto Map(SchoolSettings settings) => new(
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
        settings.IsCoranModuleEnabled);

    private static SchoolSettingsDto Defaults() => new(
        SchoolSettingsDefaults.GradingScale.ToString(),
        SchoolSettingsDefaults.StudentMatriculeFormat,
        SchoolSettingsDefaults.TeacherMatriculeFormat,
        SchoolSettingsDefaults.AutoLogoutMinutes,
        SchoolSettingsDefaults.DateFormat,
        SchoolSettingsDefaults.TuitionMonthsPerYear,
        SchoolSettingsDefaults.AllowSecretaryToManageGrading,
        SchoolSettingsDefaults.AllowFinanceToModifyFees,
        SchoolSettingsDefaults.AllowFinanceToDeleteFees,
        null,
        null,
        null,
        null,
        null,
        SchoolSettingsDefaults.TypeEtablissement.ToString(),
        SchoolSettingsDefaults.SmsAlertsEnabled,
        SchoolSettingsDefaults.SmsAlertsEnabled,
        SchoolSettingsDefaults.SmsAlertsEnabled,
        SchoolSettingsDefaults.SmsCreditBalance,
        SchoolSettingsDefaults.DebtorReminderThresholdDays,
        SchoolSettingsDefaults.IsPedagogyEnabled,
        SchoolSettingsDefaults.IsFinanceEnabled,
        SchoolSettingsDefaults.IsInternatEnabled,
        SchoolSettingsDefaults.IsCoranModuleEnabled);
}
