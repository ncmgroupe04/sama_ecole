using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Subscriptions.Changes;
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
///
/// VERROU D'ABONNEMENT : activer un module que l'abonnement ne comprend pas (l'Internat, par exemple) est une montée en
/// gamme, pas un réglage. La tentative est INTERCEPTÉE avant toute écriture (SubscriptionChangeRequiredException, 409) et
/// devient une demande que le Super Admin valide. Couper un module, ou réactiver un module souscrit, reste un réglage
/// libre ; un module déjà actif dans les réglages n'est pas « activé » à nouveau par un simple enregistrement.
/// </summary>
public class UpdateSchoolSettingsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    SubscriptionChangeService subscriptionChanges,
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

        // Verrou : seules les ACTIVATIONS nouvelles de module sont soumises à l'abonnement. Le planificateur les compare aux
        // modules souscrits : un module inclus passe, un module non souscrit déclenche l'aperçu du tarif ajusté.
        await subscriptionChanges.EnsureNoChangeAsync(
            new SubscriptionChangeSet(
                IsPedagogyEnabled: request.IsPedagogyEnabled && !settings.IsPedagogyEnabled ? true : null,
                IsFinanceEnabled: request.IsFinanceEnabled && !settings.IsFinanceEnabled ? true : null,
                IsInternatEnabled: request.IsInternatEnabled && !settings.IsInternatEnabled ? true : null,
                IsCoranModuleEnabled: request.IsCoranModuleEnabled && !settings.IsCoranModuleEnabled ? true : null),
            cancellationToken);

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

        settings.IsPedagogyEnabled = request.IsPedagogyEnabled;
        settings.IsFinanceEnabled = request.IsFinanceEnabled;
        settings.IsInternatEnabled = request.IsInternatEnabled;
        settings.IsCoranModuleEnabled = request.IsCoranModuleEnabled;
        settings.GradeEditWindowDays = request.GradeEditWindowDays;

        // Valeur inconnue : refusée en amont par le validateur ; le parse ne sert donc que de conversion.
        settings.EvaluationPeriodType = Enum.Parse<EvaluationPeriodType>(request.EvaluationPeriodType, ignoreCase: true);
        settings.CustomPeriodCount = request.CustomPeriodCount;

        // null = « inchangé » : c'est ce réglage qui verrouille des saisies, un ancien client qui ne
        // l'envoie pas ne doit pas le réinitialiser. Valeur invalide : refusée en amont par le validateur.
        if (request.WorkingDays is not null)
        {
            settings.WorkingDays = SchoolWeek.Serialize(SchoolWeek.TryParse(request.WorkingDays)!);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Paramètres de l'établissement {SchoolId} mis à jour.", schoolId);

        return SchoolSettingsDtoMapper.From(settings);
    }
}
