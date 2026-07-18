using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.UpdateGradingScale;

/// <summary>
/// Ticket JGK-G02 — met à jour UNIQUEMENT le barème de l'établissement courant. Les autres réglages
/// (matricules, déconnexion automatique, mensualités) ne sont ni lus ni écrits ici : voir
/// UpdateGradingScaleCommand pour la raison de cet endpoint séparé.
/// </summary>
public class UpdateGradingScaleCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ILogger<UpdateGradingScaleCommandHandler> logger)
    : IRequestHandler<UpdateGradingScaleCommand, SchoolSettingsDto>
{
    public async Task<SchoolSettingsDto> Handle(
        UpdateGradingScaleCommand request,
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

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Barème de l'établissement {SchoolId} mis à jour à {GradingScale}.", schoolId, settings.GradingScale);

        return new SchoolSettingsDto(
            settings.GradingScale.ToString(),
            settings.StudentMatriculeFormat,
            settings.TeacherMatriculeFormat,
            settings.AutoLogoutMinutes,
            settings.DateFormat,
            settings.TuitionMonthsPerYear);
    }
}
