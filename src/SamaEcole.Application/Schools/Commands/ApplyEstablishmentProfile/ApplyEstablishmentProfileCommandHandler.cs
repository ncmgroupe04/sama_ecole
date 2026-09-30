using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;

/// <summary>
/// Onboarding — enregistre le profil choisi par le Directeur ET applique le PRESET de modules associé
/// (voir EstablishmentProfilePresets) en une seule transaction : un profil sans son preset laisserait
/// l'établissement dans un état incohérent (ex. Daara/Internat choisi mais Internat resté désactivé).
/// </summary>
public class ApplyEstablishmentProfileCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ILogger<ApplyEstablishmentProfileCommandHandler> logger)
    : IRequestHandler<ApplyEstablishmentProfileCommand, SchoolSettingsDto>
{
    public async Task<SchoolSettingsDto> Handle(
        ApplyEstablishmentProfileCommand request,
        CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Valeur validée en amont : à ce stade, elle correspond forcément à un membre de l'enum.
        var profile = Enum.Parse<ProfileEtablissement>(request.Profile, ignoreCase: true);
        var preset = EstablishmentProfilePresets.For(profile);

        var settings = await dbContext.SchoolSettings.FirstOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = new SchoolSettings { SchoolId = schoolId };
            dbContext.SchoolSettings.Add(settings);
        }

        settings.ProfileEtablissement = profile;
        settings.IsPedagogyEnabled = preset.IsPedagogyEnabled;
        settings.IsFinanceEnabled = preset.IsFinanceEnabled;
        settings.IsInternatEnabled = preset.IsInternatEnabled;
        settings.IsCoranModuleEnabled = preset.IsCoranModuleEnabled;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Profil d'établissement {Profile} appliqué à l'école {SchoolId}.", profile, schoolId);

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
            settings.DebtorReminderThresholdDays,
            settings.IsPedagogyEnabled,
            settings.IsFinanceEnabled,
            settings.IsInternatEnabled,
            settings.IsCoranModuleEnabled,
            settings.GradeEditWindowDays,
            settings.EvaluationPeriodType.ToString(),
            settings.CustomPeriodCount,
            SchoolWeek.ToNames(SchoolWeek.FromStored(settings.WorkingDays)),
            settings.ProfileEtablissement?.ToString());
    }
}
