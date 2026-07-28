using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

public static class TaxCalculator
{
    public static TaxeDeclaration CalculateTaxDeclaration(
        Guid schoolId, 
        int month, 
        int year, 
        List<FichePaie> fichesPaie, 
        decimal tvaCollected, 
        decimal tvaDeductible)
    {
        // 1. Agréger les fiches de paie pour les charges sociales
        decimal totalIpresEmployee = fichesPaie.Sum(f => f.IpresEmployee);
        decimal totalIpresEmployer = fichesPaie.Sum(f => f.IpresEmployer);
        decimal totalIpres = totalIpresEmployee + totalIpresEmployer;
        
        decimal totalCss = fichesPaie.Sum(f => f.CssEmployer);
        decimal totalVrs = fichesPaie.Sum(f => f.Vrs);
        decimal totalBrs = fichesPaie.Sum(f => f.Brs);

        // 2. Calcul TVA
        decimal netTva = tvaCollected - tvaDeductible;

        // 3. Calcul total dû à l'État
        // IPRES et CSS vont aux caisses de sécurité sociale
        // VRS, BRS, TVA vont aux Impôts (VRS = CFCE, BRS = Retenue sur salaire)
        decimal totalDueToState = totalVrs + totalBrs + (netTva > 0 ? netTva : 0m);

        return new TaxeDeclaration
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            Month = month,
            Year = year,
            TotalIpres = totalIpres,
            TotalCss = totalCss,
            TotalVrs = totalVrs,
            TotalBrs = totalBrs,
            TvaCollected = tvaCollected,
            TvaDeductible = tvaDeductible,
            NetTva = netTva,
            TotalDueToState = totalDueToState
        };
    }
}
