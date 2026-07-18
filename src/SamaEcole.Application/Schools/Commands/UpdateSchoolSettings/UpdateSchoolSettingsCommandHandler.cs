using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
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

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Paramètres de l'établissement {SchoolId} mis à jour.", schoolId);

        return new SchoolSettingsDto(
            settings.GradingScale.ToString(),
            settings.StudentMatriculeFormat,
            settings.TeacherMatriculeFormat,
            settings.AutoLogoutMinutes,
            settings.DateFormat,
            settings.TuitionMonthsPerYear,
            settings.AllowSecretaryToManageGrading);
    }
}
