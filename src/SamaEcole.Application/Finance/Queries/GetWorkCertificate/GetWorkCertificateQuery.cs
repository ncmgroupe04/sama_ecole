using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetWorkCertificate;

public record GetWorkCertificateQuery(Guid ContractId) : IRequest<WorkCertificateDto>;

/// <summary>
/// Attestation de travail — document RH interne (comme le bulletin de paie, <see cref="SamaEcole.Application.Finance.Queries.GetPayslip.PayslipDto"/>) :
/// pas de bandeau M.E.N., juste l'établissement en tant qu'employeur. <see cref="SinceDate"/> est la
/// date de création du contrat (<c>EmployeeContract.CreatedAt</c>) — la seule date d'engagement connue
/// du système, aucune colonne "date d'embauche" distincte n'existe (AGENTS.md : ne jamais inventer une
/// donnée absente).
/// </summary>
public record WorkCertificateDto(
    Guid ContractId,
    string CertificateNumber,
    string EmployeeFullName,
    string EmployeeRole,
    string ContractType,
    DateTimeOffset SinceDate,
    DateTimeOffset IssuedAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolPhone,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public class GetWorkCertificateQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetWorkCertificateQuery, WorkCertificateDto>
{
    public async Task<WorkCertificateDto> Handle(GetWorkCertificateQuery request, CancellationToken cancellationToken)
    {
        // Jointure explicite sur School via c.SchoolId : School n'implémente PAS ITenantEntity (elle
        // définit le tenant, elle ne lui appartient pas — voir School.cs), donc dbContext.Schools n'a
        // AUCUN filtre automatique. Lire "la" school sans cette jointure exposerait potentiellement
        // celle d'un autre établissement (AGENTS.md règle #2) : c'est l'appartenance du CONTRAT
        // (déjà borné par le Global Query Filter + RLS, lui, ITenantEntity) qui fixe l'école correcte.
        var row = await (
            from c in dbContext.EmployeeContracts.AsNoTracking()
            join sch in dbContext.Schools.AsNoTracking() on c.SchoolId equals sch.Id
            where c.Id == request.ContractId
            select new
            {
                c.Id,
                c.Type,
                c.CreatedAt,
                EmployeeFullName = c.Teacher != null ? c.Teacher.FullName : c.User!.FullName,
                EmployeeRole = c.Teacher != null ? "Enseignant" : c.User!.Role.ToString(),
                School = sch
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat introuvable : {request.ContractId}");

        return new WorkCertificateDto(
            row.Id,
            $"AT-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.EmployeeFullName,
            row.EmployeeRole,
            row.Type.ToString(),
            row.CreatedAt,
            timeProvider.GetUtcNow(),
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Phone,
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
