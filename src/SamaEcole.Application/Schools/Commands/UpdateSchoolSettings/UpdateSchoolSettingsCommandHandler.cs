using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.UpdateSchoolSettings;

/// <summary>
/// Ticket JGK-B02 — mise à jour des paramètres de l'établissement COURANT.
///
/// Crée la ligne si elle n'existe pas : les écoles antérieures à ce ticket n'en ont pas, et leur
/// Directeur doit pouvoir régler ses paramètres sans qu'on aille bricoler la base à la main.
///
/// L'INSERT passe la policy RLS sans porte dérobée : un Directeur A UN tenant, contrairement au
/// Super Admin — le WITH CHECK est donc satisfait par construction.
/// </summary>
public class UpdateSchoolSettingsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ILogger<UpdateSchoolSettingsCommandHandler> logger)
    : IRequestHandler<UpdateSchoolSettingsCommand, SchoolSettingsDto>
{
    public async Task<SchoolSettingsDto> Handle(
        UpdateSchoolSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var settings = await dbContext.SchoolSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new SchoolSettings { SchoolId = schoolId };
            dbContext.SchoolSettings.Add(settings);
        }

        // Le barème est validé en amont : à ce stade, il vaut « 10 » ou « 20 ».
        settings.GradingScale = int.Parse(request.GradingScale);
        settings.StudentMatriculeFormat = request.StudentMatriculeFormat.Trim();
        settings.TeacherMatriculeFormat = request.TeacherMatriculeFormat.Trim();
        settings.AutoLogoutMinutes = request.AutoLogoutMinutes;
        settings.DateFormat = request.DateFormat;
        settings.TuitionMonthsPerYear = request.TuitionMonthsPerYear;
        settings.AllowSecretaryToManageGrading = request.AllowSecretaryToManageGrading;
        settings.AllowFinanceToModifyFees = request.AllowFinanceToModifyFees;
        settings.AllowFinanceToDeleteFees = request.AllowFinanceToDeleteFees;
        settings.DirectorSignatureUrl = string.IsNullOrWhiteSpace(request.DirectorSignatureUrl) ? null : request.DirectorSignatureUrl.Trim();
        settings.SecretarySignatureUrl = string.IsNullOrWhiteSpace(request.SecretarySignatureUrl) ? null : request.SecretarySignatureUrl.Trim();
        settings.CashierSignatureUrl = string.IsNullOrWhiteSpace(request.CashierSignatureUrl) ? null : request.CashierSignatureUrl.Trim();
        settings.OfficialStampUrl = string.IsNullOrWhiteSpace(request.OfficialStampUrl) ? null : request.OfficialStampUrl.Trim();
        settings.SurveillantSignatureUrl = string.IsNullOrWhiteSpace(request.SurveillantSignatureUrl) ? null : request.SurveillantSignatureUrl.Trim();

        // TypeEtablissement : parse sécurisé — valeur invalide silencieusement ramenée à Prive (défaut).
        settings.TypeEtablissement = Enum.TryParse<TypeEtablissement>(request.TypeEtablissement, ignoreCase: true, out var typeResult)
            ? typeResult
            : TypeEtablissement.Prive;

        // SmsCreditBalance n'est PAS repris de la requête : il n'y figure pas (voir la commande).
        // L'affectation ci-dessous porte uniquement sur les commutateurs d'activation.
        settings.SmsOnAttendanceAlert = request.SmsOnAttendanceAlert;
        settings.SmsOnDuesReminder = request.SmsOnDuesReminder;
        settings.SmsOnPaymentReceipt = request.SmsOnPaymentReceipt;
        settings.DebtorReminderThresholdDays = request.DebtorReminderThresholdDays;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Paramètres de l'établissement {SchoolId} mis à jour.", schoolId);

        return new SchoolSettingsDto(
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
            settings.DebtorReminderThresholdDays);
    }
}
