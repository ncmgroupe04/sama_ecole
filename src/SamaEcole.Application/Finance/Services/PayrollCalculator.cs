using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

public static class PayrollCalculator
{
    // Taux standards au Sénégal (à confirmer/modifier si besoin)
    public const decimal IpresEmployeeRate = 0.056m;
    public const decimal IpresEmployerRate = 0.084m;
    public const decimal CssEmployerRate = 0.07m;
    public const decimal CfmEmployerRate = 0.03m; // Contribution Forfaitaire à la charge de l'Employeur (souvent appelée CFCE)
    public const decimal BrsRate = 0.05m; // BRS (retenue à la source) simplifié
    
    // Plafonds (Exemples, souvent mis à jour)
    public const decimal IpresCeiling = 360000m; // Plafond mensuel IPRES RG
    public const decimal CssCeiling = 63000m;    // Plafond mensuel CSS Prestations Familiales

    public static FichePaie CalculateFichePaie(
        Guid schoolId, 
        Guid contractId, 
        int month, 
        int year, 
        decimal baseSalary, 
        decimal hourlyRate, 
        decimal hoursWorked,
        decimal transportAllowance,
        bool applyDeductions = true)
    {
        // Calcul du Brut
        decimal grossSalary = hourlyRate > 0
            ? hoursWorked * hourlyRate
            : baseSalary;

        // Paie « sans retenues » (structure qui ne cotise pas, ex. un Daara) : aucune cotisation, aucune charge, aucune
        // retenue — la personne reçoit exactement son salaire brut plus sa prime de transport. Les deux régimes
        // coexistent dans une même école : c'est le contrat de la personne qui choisit (EmployeeContract.ApplyStatutoryDeductions).
        if (!applyDeductions)
        {
            return new FichePaie
            {
                Id = Guid.NewGuid(),
                SchoolId = schoolId,
                EmployeeContractId = contractId,
                Month = month,
                Year = year,
                HoursWorked = hoursWorked,
                GrossSalary = grossSalary,
                TransportAllowance = transportAllowance,
                IpresEmployee = 0m,
                IpresEmployer = 0m,
                CssEmployer = 0m,
                Vrs = 0m,
                Brs = 0m,
                NetSalary = grossSalary + transportAllowance,
                DeductionsApplied = false
            };
        }

        // Base IPRES plafonnée
        decimal ipresBase = Math.Min(grossSalary, IpresCeiling);
        
        // Base CSS plafonnée
        decimal cssBase = Math.Min(grossSalary, CssCeiling);

        decimal ipresEmployee = ipresBase * IpresEmployeeRate;
        decimal ipresEmployer = ipresBase * IpresEmployerRate;
        decimal cssEmployer = cssBase * CssEmployerRate;
        
        // CFCE/VRS (Employeur)
        decimal vrs = grossSalary * CfmEmployerRate; 

        // BRS (Employé) - Retenue à la source
        // Calcul très simplifié: base imposable = Brut - IPRES employé
        decimal taxableBase = grossSalary - ipresEmployee;
        decimal brs = taxableBase > 0 ? taxableBase * BrsRate : 0m; 

        // Calcul du net
        decimal netSalary = grossSalary + transportAllowance - ipresEmployee - brs;

        return new FichePaie
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            EmployeeContractId = contractId,
            Month = month,
            Year = year,
            HoursWorked = hoursWorked,
            GrossSalary = grossSalary,
            TransportAllowance = transportAllowance,
            IpresEmployee = ipresEmployee,
            IpresEmployer = ipresEmployer,
            CssEmployer = cssEmployer,
            Vrs = vrs,
            Brs = brs,
            NetSalary = netSalary
        };
    }
}
