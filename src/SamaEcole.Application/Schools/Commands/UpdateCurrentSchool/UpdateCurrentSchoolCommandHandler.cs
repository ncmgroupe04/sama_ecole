using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;

/// <summary>
/// Met à jour l'identité de l'établissement COURANT. <c>schools</c> échappe à la RLS (elle définit le
/// tenant) : rien en base ne cantonne l'UPDATE à la bonne école. On charge donc STRICTEMENT l'école
/// du tenant du JWT (jamais une école désignée par le client, règle #10) — c'est CE filtre applicatif
/// qui tient lieu d'isolation ici.
///
/// Ces champs (nom, coordonnées, logo) ne sont pas des données financières : la règle #4 ne s'y
/// applique pas. Ils ne modifient aucun montant d'inscription.
/// </summary>
public class UpdateCurrentSchoolCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ILogger<UpdateCurrentSchoolCommandHandler> logger)
    : IRequestHandler<UpdateCurrentSchoolCommand, SchoolProfileDto>
{
    public async Task<SchoolProfileDto> Handle(UpdateCurrentSchoolCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var school = await dbContext.Schools.FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Établissement introuvable.");

        school.Name = request.Name.Trim();
        school.Address = Normalize(request.Address);
        school.Phone = Normalize(request.Phone);
        school.LogoUrl = Normalize(request.LogoUrl);
        school.InspectionAcademie = Normalize(request.InspectionAcademie);
        school.InspectionEducationFormation = Normalize(request.InspectionEducationFormation);
        school.NomLycee = Normalize(request.NomLycee);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Identité de l'établissement {SchoolId} mise à jour.", schoolId);

        return new SchoolProfileDto(
            school.Name, school.Address, school.Phone, school.LogoUrl,
            school.InspectionAcademie, school.InspectionEducationFormation, school.NomLycee);
    }

    /// <summary>Chaîne vide ⇒ null : un champ optionnel effacé par le Directeur redevient NULL, pas "".</summary>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
