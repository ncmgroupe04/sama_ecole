using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetTaxDeclaration;

public record GetTaxDeclarationQuery(Guid TaxDeclarationId) : IRequest<TaxDeclarationDto>;

/// <summary>
/// Déclaration fiscale mensuelle imprimable — document Finance interne (comme le bulletin de paie) :
/// pas de bandeau M.E.N., établissement en tant que déclarant.
/// </summary>
public record TaxDeclarationDto(
    Guid Id,
    string DeclarationNumber,
    int Month,
    int Year,
    decimal TotalIpres,
    decimal TotalCss,
    decimal TotalVrs,
    decimal TotalBrs,
    decimal TvaCollected,
    decimal TvaDeductible,
    decimal NetTva,
    decimal TotalDueToState,
    DateTimeOffset CreatedAt,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolLogoUrl);

public class GetTaxDeclarationQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetTaxDeclarationQuery, TaxDeclarationDto>
{
    public async Task<TaxDeclarationDto> Handle(GetTaxDeclarationQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from t in dbContext.TaxeDeclarations.AsNoTracking()
            join sch in dbContext.Schools.AsNoTracking() on t.SchoolId equals sch.Id
            where t.Id == request.TaxDeclarationId
            select new { Declaration = t, School = sch })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Déclaration fiscale introuvable : {request.TaxDeclarationId}");

        return new TaxDeclarationDto(
            row.Declaration.Id,
            $"DECL-{row.Declaration.Id.ToString()[..8].ToUpperInvariant()}-{row.Declaration.Year}{row.Declaration.Month:D2}",
            row.Declaration.Month,
            row.Declaration.Year,
            row.Declaration.TotalIpres,
            row.Declaration.TotalCss,
            row.Declaration.TotalVrs,
            row.Declaration.TotalBrs,
            row.Declaration.TvaCollected,
            row.Declaration.TvaDeductible,
            row.Declaration.NetTva,
            row.Declaration.TotalDueToState,
            row.Declaration.CreatedAt,
            row.School.Name,
            row.School.Address,
            ReceiptCity.FromAddress(row.School.Address),
            row.School.Ninea,
            row.School.LogoUrl);
    }
}
