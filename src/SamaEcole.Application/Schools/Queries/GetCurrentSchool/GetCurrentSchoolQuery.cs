using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Schools.Queries.GetCurrentSchool;

/// <summary>
/// GET /schools/current — identité de l'établissement de l'appelant. « current » vient du JWT, jamais
/// d'un paramètre (AGENTS.md règle #10).
///
/// La table <c>schools</c> échappe volontairement à la RLS (elle DÉFINIT le tenant) et n'a pas de
/// Global Query Filter : on filtre donc EXPLICITEMENT sur l'Id du tenant courant, sans quoi on lirait
/// une école au hasard. Lecture ouverte à tout rôle de l'école — l'en-tête du reçu et les écrans en
/// ont besoin.
/// </summary>
public record GetCurrentSchoolQuery : IRequest<SchoolProfileDto>;

public class GetCurrentSchoolQueryHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<GetCurrentSchoolQuery, SchoolProfileDto>
{
    public async Task<SchoolProfileDto> Handle(GetCurrentSchoolQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken)
            ?? throw new KeyNotFoundException("Établissement introuvable.");

        return new SchoolProfileDto(
            school.Name, school.Address, school.Phone, school.LogoUrl,
            school.InspectionAcademie, school.InspectionEducationFormation, school.NomLycee,
            school.Email, school.Ninea, school.RegistreCommerce);
    }
}
